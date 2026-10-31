namespace Tamp.SpfxCli;

/// <summary>
/// Facade for the SharePoint Framework CLI (<c>@microsoft/spfx-cli</c>, command <c>spfx</c>): scaffolding
/// solutions and components from templates. Building, testing and packaging an SPFx solution is
/// <c>Tamp.Spfx</c>'s job; this package only creates them.
/// </summary>
public static class SpfxCli
{
    /// <summary><c>spfx create</c>: scaffold a solution, or add a component to an existing one.</summary>
    public static CommandPlan Create(Action<SpfxCreateSettings> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var s = new SpfxCreateSettings();
        configure(s);
        return s.ToCommandPlan();
    }

    /// <summary><c>spfx create</c>, object-init form.</summary>
    public static CommandPlan Create(SpfxCreateSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.ToCommandPlan();
    }

    /// <summary><c>spfx list-templates</c>: print the templates the configured sources offer (a human-readable table).</summary>
    public static CommandPlan ListTemplates(Action<SpfxListTemplatesSettings>? configure = null)
    {
        var s = new SpfxListTemplatesSettings();
        configure?.Invoke(s);
        return s.ToCommandPlan();
    }

    /// <summary><c>spfx list-templates</c>, object-init form.</summary>
    public static CommandPlan ListTemplates(SpfxListTemplatesSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.ToCommandPlan();
    }

    /// <summary>
    /// Escape hatch: <c>spfx &lt;args…&gt;</c> with the same binary resolution and no validation. The
    /// environment is left exactly as inherited, so none of the protections in
    /// <see cref="SpfxCliSettingsBase"/> apply.
    /// </summary>
    public static CommandPlan Raw(string? toolDirectory, params string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var resolution = SpfxCliResolver.TryResolve(toolDirectory ?? Directory.GetCurrentDirectory())
            ?? throw new InvalidOperationException("The SharePoint Framework CLI (@microsoft/spfx-cli) was not found.");
        var args = new List<string>(resolution.PrefixArguments);
        args.AddRange(arguments);
        return new CommandPlan { Executable = resolution.Executable, Arguments = args, WorkingDirectory = toolDirectory };
    }
}

/// <summary>Fluent setters for the <c>spfx</c> settings types.</summary>
public static class SpfxCliSettingsExtensions
{
    /// <summary>Set the process working directory.</summary>
    public static T SetWorkingDirectory<T>(this T s, string? directory) where T : SpfxCliSettingsBase { s.WorkingDirectory = directory; return s; }

    /// <summary>Set where to look for an installed <c>@microsoft/spfx-cli</c>.</summary>
    public static T SetToolDirectory<T>(this T s, string? directory) where T : SpfxCliSettingsBase { s.ToolDirectory = directory; return s; }

    /// <summary>Add an environment variable for the child process.</summary>
    public static T SetEnvironmentVariable<T>(this T s, string name, string value) where T : SpfxCliSettingsBase { s.EnvironmentVariables[name] = value; return s; }

    /// <summary>Use an explicit CLI resolution.</summary>
    public static T SetBinary<T>(this T s, SpfxCliResolution? binary) where T : SpfxCliSettingsBase { s.Binary = binary; return s; }

    /// <summary>Append a raw argument.</summary>
    public static T AddArgument<T>(this T s, string argument) where T : SpfxCliSettingsBase { s.ExtraArguments.Add(argument); return s; }

    /// <summary><c>--template-url</c>.</summary>
    public static T SetTemplateUrl<T>(this T s, string? url) where T : SpfxCliSettingsBase { s.TemplateUrl = url; return s; }

    /// <summary><c>--spfx-version</c> (a <c>version/&lt;value&gt;</c> branch).</summary>
    public static T SetSpfxVersion<T>(this T s, string? version) where T : SpfxCliSettingsBase { s.SpfxVersion = version; return s; }

    /// <summary>Pin the template source to a commit SHA.</summary>
    public static T SetTemplateCommit<T>(this T s, string? sha) where T : SpfxCliSettingsBase { s.TemplateCommit = sha; return s; }

    /// <summary><c>--local-source</c>.</summary>
    public static T AddLocalSource<T>(this T s, string path) where T : SpfxCliSettingsBase { s.LocalSources.Add(path); return s; }

    /// <summary><c>--remote-source</c>.</summary>
    public static T AddRemoteSource<T>(this T s, string url) where T : SpfxCliSettingsBase { s.RemoteSources.Add(url); return s; }

    /// <summary>GitHub token, passed through the environment and redacted.</summary>
    public static T SetGitHubToken<T>(this T s, Secret? token) where T : SpfxCliSettingsBase { s.GitHubToken = token; return s; }

    /// <summary>Allow the GitHub token to be sent to another host (a GitHub Enterprise server).</summary>
    public static T AddGitHubTokenHost<T>(this T s, string host) where T : SpfxCliSettingsBase { s.GitHubTokenHosts.Add(host); return s; }

    /// <summary>Refuse unpinned template sources.</summary>
    public static T SetRequirePinnedTemplates<T>(this T s, bool value = true) where T : SpfxCliSettingsBase { s.RequirePinnedTemplates = value; return s; }

    /// <summary><c>SPFX_CI_MODE=1</c>: fixed fake ids, for template golden-file tests only.</summary>
    public static T SetDeterministicIds<T>(this T s, bool value = true) where T : SpfxCliSettingsBase { s.DeterministicIds = value; return s; }

    /// <summary><c>--template</c>.</summary>
    public static SpfxCreateSettings SetTemplate(this SpfxCreateSettings s, string template) { s.Template = template; return s; }

    /// <summary><c>--library-name</c>.</summary>
    public static SpfxCreateSettings SetLibraryName(this SpfxCreateSettings s, string name) { s.LibraryName = name; return s; }

    /// <summary><c>--component-name</c>.</summary>
    public static SpfxCreateSettings SetComponentName(this SpfxCreateSettings s, string name) { s.ComponentName = name; return s; }

    /// <summary><c>--component-alias</c>.</summary>
    public static SpfxCreateSettings SetComponentAlias(this SpfxCreateSettings s, string? alias) { s.ComponentAlias = alias; return s; }

    /// <summary><c>--component-description</c>.</summary>
    public static SpfxCreateSettings SetComponentDescription(this SpfxCreateSettings s, string? description) { s.ComponentDescription = description; return s; }

    /// <summary><c>--solution-name</c>.</summary>
    public static SpfxCreateSettings SetSolutionName(this SpfxCreateSettings s, string? name) { s.SolutionName = name; return s; }

    /// <summary><c>--target-dir</c>.</summary>
    public static SpfxCreateSettings SetTargetDirectory(this SpfxCreateSettings s, string? directory) { s.TargetDirectory = directory; return s; }

    /// <summary><c>--package-manager</c>.</summary>
    public static SpfxCreateSettings SetPackageManager(this SpfxCreateSettings s, SpfxPackageManager packageManager) { s.PackageManager = packageManager; return s; }
}
