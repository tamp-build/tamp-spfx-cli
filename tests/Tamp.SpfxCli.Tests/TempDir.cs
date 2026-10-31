namespace Tamp.SpfxCli.Tests;

/// <summary>A throwaway directory, optionally with a fake @microsoft/spfx-cli install.</summary>
internal sealed class TempDir : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "tamp-spfx-cli-tests", Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Root);

    public string Dir(params string[] parts)
    {
        var p = Path.Combine(new[] { Root }.Concat(parts).ToArray());
        Directory.CreateDirectory(p);
        return p;
    }

    /// <summary>Write a fake @microsoft/spfx-cli package into <paramref name="packageDir"/>; returns the bin script.</summary>
    public static string WritePackage(string packageDir, string version = "0.1.0-pre.3", string binJson = "{ \"spfx\": \"bin/spfx\" }",
        string name = "@microsoft/spfx-cli", bool writeScript = true)
    {
        Directory.CreateDirectory(Path.Combine(packageDir, "bin"));
        File.WriteAllText(Path.Combine(packageDir, "package.json"),
            $$"""{ "name": "{{name}}", "version": "{{version}}", "bin": {{binJson}} }""");
        var script = Path.Combine(packageDir, "bin", "spfx");
        if (writeScript) File.WriteAllText(script, "#!/usr/bin/env node\n");
        return script;
    }

    public string InstallLocal(string projectDir, string version = "0.1.0-pre.3") =>
        WritePackage(Path.Combine(projectDir, "node_modules", "@microsoft", "spfx-cli"), version);

    public string InstallShim(string projectDir)
    {
        var bin = Path.Combine(projectDir, "node_modules", ".bin");
        Directory.CreateDirectory(bin);
        var shim = Path.Combine(bin, OperatingSystem.IsWindows() ? "spfx.cmd" : "spfx");
        File.WriteAllText(shim, "");
        return shim;
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

internal static class Fixture
{
    public static string Path(params string[] parts) =>
        System.IO.Path.Combine(new[] { AppContext.BaseDirectory, "Fixtures" }.Concat(parts).ToArray());

    public static string Text(params string[] parts) => File.ReadAllText(Path(parts));
}
