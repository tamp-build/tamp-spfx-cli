using System.Text.Json;

namespace Tamp.SpfxCli;

/// <summary>How the <c>spfx</c> invocation was located.</summary>
public enum SpfxCliResolutionSource
{
    /// <summary>
    /// An installed <c>@microsoft/spfx-cli</c> package in <c>node_modules</c> at the tool directory or any
    /// ancestor, run as <c>node &lt;package&gt;/bin/spfx</c>. This is a tool directory with its own
    /// lockfile, which is the reproducible way to pin the CLI on a build machine.
    /// </summary>
    LocalPackage,

    /// <summary>
    /// A global install (<c>npm install -g @microsoft/spfx-cli</c>) whose package was found next to the
    /// <c>spfx</c> command on <c>PATH</c>, run through <c>node</c>.
    /// </summary>
    GlobalPackage,

    /// <summary>
    /// A <c>spfx</c> command on <c>PATH</c> (or a <c>node_modules/.bin</c> shim) whose package couldn't be
    /// located, or with no <c>node</c> on <c>PATH</c>. The version is unknown.
    /// </summary>
    Shim,

    /// <summary>The adopter supplied the resolution via <c>SetBinary</c>.</summary>
    Explicit,
}

/// <summary>A resolved <c>spfx</c> invocation.</summary>
public sealed record SpfxCliResolution
{
    /// <summary>Path of the executable to spawn (<c>node</c>, or the <c>spfx</c> shim).</summary>
    public required string Executable { get; init; }

    /// <summary>Arguments emitted before the CLI command (the bin script, when running through <c>node</c>).</summary>
    public IReadOnlyList<string> PrefixArguments { get; init; } = Array.Empty<string>();

    /// <summary>How this resolution was produced.</summary>
    public required SpfxCliResolutionSource Source { get; init; }

    /// <summary>The installed <c>@microsoft/spfx-cli</c> version, when it was read from the package (else null).</summary>
    public string? Version { get; init; }

    /// <summary>The package directory, when known.</summary>
    public string? PackageDirectory { get; init; }
}

/// <summary>
/// Locates the SharePoint Framework CLI (<c>@microsoft/spfx-cli</c>, command <c>spfx</c>). In priority order:
/// <list type="number">
///   <item>An installed package in <c>node_modules</c> at the tool directory or any ancestor, through <c>node</c>.</item>
///   <item>A global install, found from the <c>spfx</c> command on <c>PATH</c>, through <c>node</c>.</item>
///   <item>The <c>spfx</c> shim itself (local <c>node_modules/.bin</c> first, then <c>PATH</c>).</item>
///   <item><c>null</c>: not installed.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// There is no <c>npx</c> / <c>npm exec</c> fallback. The CLI is pre-release and its behaviour changes
/// between builds; a build machine should run the version it was given, not whatever the registry serves
/// today.
/// </para>
/// <para>
/// The <c>create</c> command scaffolds into a directory that has no <c>node_modules</c> yet, so the tool
/// directory is separate from the working directory (<see cref="SpfxCliSettingsBase.ToolDirectory"/>).
/// </para>
/// </remarks>
public static class SpfxCliResolver
{
    /// <summary>The npm package that provides the <c>spfx</c> command.</summary>
    public const string PackageName = "@microsoft/spfx-cli";

    /// <summary>Resolve the CLI starting at <paramref name="toolDirectory"/>, or null when it isn't installed.</summary>
    public static SpfxCliResolution? TryResolve(string toolDirectory) =>
        TryResolve(toolDirectory, FindOnPath("node"), FindOnPath(OperatingSystem.IsWindows() ? "spfx.cmd" : "spfx"));

    /// <summary>True when <see cref="TryResolve(string)"/> finds the CLI.</summary>
    public static bool IsAvailable(string toolDirectory) => TryResolve(toolDirectory) is not null;

    // Seam for tests: PATH lookups are passed in rather than read, so tests never mutate PATH.
    internal static SpfxCliResolution? TryResolve(string toolDirectory, string? node, string? globalSpfx)
    {
        if (string.IsNullOrWhiteSpace(toolDirectory))
            throw new ArgumentException("toolDirectory is required.", nameof(toolDirectory));

        string? localShim = null;
        foreach (var dir in SelfAndAncestors(Path.GetFullPath(toolDirectory)))
        {
            var modules = Path.Combine(dir, "node_modules");
            if (!Directory.Exists(modules)) continue;

            var package = Path.Combine(modules, "@microsoft", "spfx-cli");
            var bin = ReadBinScript(package, out var version);
            if (bin is not null && node is not null)
                return new SpfxCliResolution
                {
                    Executable = node,
                    PrefixArguments = new[] { bin },
                    Source = SpfxCliResolutionSource.LocalPackage,
                    Version = version,
                    PackageDirectory = Path.GetFullPath(package),
                };

            localShim ??= ShimIn(modules);
        }

        if (localShim is not null)
            return new SpfxCliResolution { Executable = localShim, Source = SpfxCliResolutionSource.Shim };

        if (globalSpfx is null) return null;

        if (node is not null && FindGlobalPackage(globalSpfx) is { } globalPackage
            && ReadBinScript(globalPackage, out var globalVersion) is { } globalBin)
            return new SpfxCliResolution
            {
                Executable = node,
                PrefixArguments = new[] { globalBin },
                Source = SpfxCliResolutionSource.GlobalPackage,
                Version = globalVersion,
                PackageDirectory = globalPackage,
            };

        return new SpfxCliResolution { Executable = globalSpfx, Source = SpfxCliResolutionSource.Shim };
    }

    /// <summary>
    /// The package directory behind a global <c>spfx</c> command. npm on Windows puts
    /// <c>spfx.cmd</c> next to <c>node_modules\@microsoft\spfx-cli</c>; on Linux and macOS
    /// <c>&lt;prefix&gt;/bin/spfx</c> is a symlink into <c>&lt;prefix&gt;/lib/node_modules/@microsoft/spfx-cli</c>.
    /// </summary>
    internal static string? FindGlobalPackage(string globalSpfx)
    {
        var binDir = Path.GetDirectoryName(Path.GetFullPath(globalSpfx));
        if (binDir is null) return null;

        var candidates = new List<string>
        {
            Path.Combine(binDir, "node_modules", "@microsoft", "spfx-cli"),
        };
        if (Path.GetDirectoryName(binDir) is { } prefix)
            candidates.Add(Path.Combine(prefix, "lib", "node_modules", "@microsoft", "spfx-cli"));

        try
        {
            if (new FileInfo(globalSpfx).ResolveLinkTarget(returnFinalTarget: true) is { } target)
            {
                // <package>/bin/spfx -> <package>
                var targetDir = Path.GetDirectoryName(target.FullName);
                if (targetDir is not null && Path.GetDirectoryName(targetDir) is { } packageDir)
                    candidates.Insert(0, packageDir);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        foreach (var c in candidates)
            if (ReadBinScript(c, out _) is not null) return Path.GetFullPath(c);
        return null;
    }

    /// <summary>
    /// The bin script of an installed <c>@microsoft/spfx-cli</c> package, from its <c>package.json</c>
    /// <c>bin</c> field (string form or <c>{"spfx": "..."}</c>). Null unless the package is really
    /// <c>@microsoft/spfx-cli</c> and the script is inside the package.
    /// </summary>
    internal static string? ReadBinScript(string packageDirectory, out string? version)
    {
        version = null;
        var manifest = Path.Combine(packageDirectory, "package.json");
        if (!File.Exists(manifest)) return null;

        string? relative;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String || name.GetString() != PackageName) return null;
            if (root.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String) version = v.GetString();
            relative = root.TryGetProperty("bin", out var binEl) ? binEl.ValueKind switch
            {
                JsonValueKind.String => binEl.GetString(),
                JsonValueKind.Object when binEl.TryGetProperty("spfx", out var s) && s.ValueKind == JsonValueKind.String => s.GetString(),
                _ => null,
            } : null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(relative)) return null;
        var script = Path.GetFullPath(Path.Combine(packageDirectory, relative));
        var packageRoot = Path.GetFullPath(packageDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!script.StartsWith(packageRoot, StringComparison.OrdinalIgnoreCase)) return null;
        return File.Exists(script) ? script : null;
    }

    private static string? ShimIn(string nodeModules)
    {
        var candidate = Path.Combine(nodeModules, ".bin", OperatingSystem.IsWindows() ? "spfx.cmd" : "spfx");
        return File.Exists(candidate) ? candidate : null;
    }

    private static IEnumerable<string> SelfAndAncestors(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
            yield return dir.FullName;
    }

    internal static string? FindOnPath(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return null;
        var names = OperatingSystem.IsWindows() && !Path.HasExtension(name) ? new[] { name + ".exe", name + ".cmd", name } : new[] { name };
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var n in names)
            {
                string candidate;
                try { candidate = Path.Combine(dir.Trim('"'), n); }
                catch (ArgumentException) { continue; }
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }
}
