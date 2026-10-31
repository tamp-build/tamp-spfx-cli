using System.Text.RegularExpressions;

namespace Tamp.SpfxCli;

/// <summary>Package manager <c>spfx create</c> runs after scaffolding (<c>--package-manager</c>).</summary>
public enum SpfxPackageManager
{
    /// <summary>Don't install (the CLI's default, and the right choice on a build machine: run <c>npm ci</c> yourself against a reviewed lockfile).</summary>
    None,

    /// <summary><c>npm install</c>.</summary>
    Npm,

    /// <summary><c>pnpm install</c>.</summary>
    Pnpm,

    /// <summary><c>yarn install</c>.</summary>
    Yarn,
}

/// <summary>
/// Shared settings for every <c>spfx</c> command: process environment, binary resolution, and the
/// template sources (<c>--template-url</c>, <c>--spfx-version</c>, <c>--local-source</c>,
/// <c>--remote-source</c>, <c>--github-token</c>).
/// </summary>
/// <remarks>
/// <para>
/// The CLI reads three environment variables that a build machine can leak into a scaffold without
/// anyone noticing. The wrapper sets each one explicitly on the child process so the inherited value never applies:
/// </para>
/// <list type="bullet">
///   <item><c>SPFX_CI_MODE</c>: <c>1</c> makes the CLI stamp a fixed fake solution id
///         (<c>22222222-2222-2222-2222-222222222222</c>) and derived component ids. Two solutions with the same
///         id overwrite each other in an app catalog. Forced to <c>0</c> unless <see cref="DeterministicIds"/>.</item>
///   <item><c>SPFX_TEMPLATE_REPO_URL</c>: silently redirects the default template source. Cleared, so only
///         <see cref="TemplateUrl"/> can change it.</item>
///   <item><c>GITHUB_TOKEN</c>: the CLI sends it to every template host. Cleared unless
///         <see cref="GitHubToken"/> is set, and then only allowed for <see cref="GitHubTokenHosts"/>.</item>
/// </list>
/// </remarks>
public abstract class SpfxCliSettingsBase
{
    /// <summary>The default template repository, used when no <see cref="TemplateUrl"/> is given.</summary>
    public const string DefaultTemplateRepository = "https://github.com/SharePoint/spfx";

    /// <summary>The solution id the CLI writes in its deterministic (<c>SPFX_CI_MODE=1</c>) mode.</summary>
    public const string DeterministicSolutionId = "22222222-2222-2222-2222-222222222222";

    private static readonly Regex RepoUrl = new(
        @"^https://(?<host>[^/\s]+)/(?<owner>[^/\s]+)/(?<repo>[^/\s]+?)(?:\.git)?(?:/tree/(?<ref>[^/\s]+))?/?$",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture);

    private static readonly Regex CommitSha = new("^[0-9a-fA-F]{40}$", RegexOptions.CultureInvariant);
    private static readonly Regex BranchName = new(@"^[A-Za-z0-9._\-/]+$", RegexOptions.CultureInvariant);

    /// <summary>Working directory for the process.</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Where to start looking for an installed <c>@microsoft/spfx-cli</c> (a tool directory with its own
    /// <c>package.json</c> and lockfile). Defaults to <see cref="WorkingDirectory"/>, then the current directory.
    /// </summary>
    public string? ToolDirectory { get; set; }

    /// <summary>Per-invocation environment variables (applied before the three the wrapper controls).</summary>
    public Dictionary<string, string> EnvironmentVariables { get; } = new(StringComparer.Ordinal);

    /// <summary>Explicit CLI resolution. When null, <see cref="SpfxCliResolver.TryResolve(string)"/> is used.</summary>
    public SpfxCliResolution? Binary { get; set; }

    /// <summary>Arguments appended after the typed flags, for parameters this wrapper doesn't model yet. One argv token each.</summary>
    public List<string> ExtraArguments { get; } = new();

    /// <summary>
    /// <c>--template-url</c>: the GitHub (or GitHub Enterprise) repository the templates come from. HTTPS only.
    /// May carry <c>/tree/&lt;ref&gt;</c>.
    /// </summary>
    public string? TemplateUrl { get; set; }

    /// <summary>
    /// <c>--spfx-version</c>: pick the template repository's <c>version/&lt;value&gt;</c> branch (for example
    /// <c>1.22</c> or <c>next</c>). A branch moves; for a reproducible scaffold use <see cref="TemplateCommit"/>.
    /// </summary>
    public string? SpfxVersion { get; set; }

    /// <summary>
    /// Pin the template source to one commit (a 40-character SHA). Emitted as
    /// <c>--template-url &lt;repo&gt;/tree/&lt;sha&gt;</c>, which the CLI downloads as that exact tree. Can't be
    /// combined with <see cref="SpfxVersion"/>, which would take precedence over it.
    /// </summary>
    public string? TemplateCommit { get; set; }

    /// <summary><c>--local-source</c>: a local template folder (repeatable). A reviewed, vendored copy is the offline, fully pinned option.</summary>
    public List<string> LocalSources { get; } = new();

    /// <summary><c>--remote-source</c>: an additional public GitHub template repository (repeatable). Use <c>/tree/&lt;sha&gt;</c> to pin it.</summary>
    public List<string> RemoteSources { get; } = new();

    /// <summary>
    /// Token for GitHub Enterprise or private template repositories. Passed as <c>GITHUB_TOKEN</c> in the child
    /// environment (never on the command line) and registered for redaction.
    /// </summary>
    public Secret? GitHubToken { get; set; }

    /// <summary>
    /// Hosts the <see cref="GitHubToken"/> may be sent to. The CLI sends the token to every template source,
    /// so a token is refused when any source is on another host. Default: <c>github.com</c>.
    /// </summary>
    public List<string> GitHubTokenHosts { get; } = new() { "github.com" };

    /// <summary>
    /// Refuse to run unless every template source is pinned: a <see cref="TemplateCommit"/>, a
    /// <c>/tree/&lt;sha&gt;</c> URL, or local sources only. Turn this on for machine-controlled pipelines.
    /// </summary>
    public bool RequirePinnedTemplates { get; set; }

    /// <summary>
    /// Run with <c>SPFX_CI_MODE=1</c>: fixed fake solution id and ids derived from the component alias. Only
    /// for golden-file tests of templates. Never for a solution that will be deployed.
    /// </summary>
    public bool DeterministicIds { get; set; }

    /// <summary>The CLI command (<c>create</c>, <c>list-templates</c>).</summary>
    protected abstract string Command { get; }

    /// <summary>
    /// Whether this command consults the GitHub template source given the current settings. <c>create</c>
    /// skips it when local sources are given; <c>list-templates</c> always adds it.
    /// </summary>
    protected abstract bool UsesGitHubSource { get; }

    /// <summary>Append the command-specific flags.</summary>
    protected abstract void AppendArguments(List<string> args);

    /// <summary>The effective <c>--template-url</c> value, after applying <see cref="TemplateCommit"/>.</summary>
    public string? EffectiveTemplateUrl
    {
        get
        {
            if (TemplateCommit is null) return TemplateUrl?.Trim();
            var repo = (TemplateUrl ?? DefaultTemplateRepository).Trim().TrimEnd('/');
            if (repo.EndsWith(".git", StringComparison.Ordinal)) repo = repo[..^4];
            return $"{repo}/tree/{TemplateCommit.Trim()}";
        }
    }

    /// <summary>Throw when the settings can't produce a valid, safe command line.</summary>
    protected virtual void Validate()
    {
        foreach (var a in ExtraArguments)
            if (a is null) throw new InvalidOperationException("ExtraArguments must not contain null.");

        if (TemplateCommit is not null)
        {
            if (!CommitSha.IsMatch(TemplateCommit.Trim()))
                throw new InvalidOperationException($"TemplateCommit '{TemplateCommit}' is not a 40-character commit SHA.");
            if (SpfxVersion is not null)
                throw new InvalidOperationException("TemplateCommit and SpfxVersion can't both be set: the CLI lets --spfx-version override the pinned commit.");
            if (TemplateUrl is not null && ParseRepoUrl(TemplateUrl)?.Ref is not null)
                throw new InvalidOperationException("TemplateUrl already names a /tree/<ref>; drop it or drop TemplateCommit.");
        }

        if (SpfxVersion is not null && (SpfxVersion.Trim().Length == 0 || !BranchName.IsMatch(SpfxVersion.Trim())))
            throw new InvalidOperationException($"SpfxVersion '{SpfxVersion}' is not a branch suffix (for example '1.22' or 'next').");

        if (TemplateUrl is not null && ParseRepoUrl(TemplateUrl) is null)
            throw new InvalidOperationException($"TemplateUrl '{TemplateUrl}' is not an https://<host>/<owner>/<repo>[/tree/<ref>] URL.");

        foreach (var r in RemoteSources)
            if (r is null || ParseRepoUrl(r) is null)
                throw new InvalidOperationException($"Remote source '{r}' is not an https://<host>/<owner>/<repo>[/tree/<ref>] URL.");

        foreach (var l in LocalSources)
            if (string.IsNullOrWhiteSpace(l))
                throw new InvalidOperationException("LocalSources must not contain empty paths.");

        if (GitHubToken is not null)
        {
            foreach (var host in GitHubSourceHosts())
                if (!GitHubTokenHosts.Any(h => string.Equals(h, host, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException(
                        $"The GitHub token would be sent to '{host}', which isn't in GitHubTokenHosts. The CLI sends the token to every template source.");
        }

        if (RequirePinnedTemplates)
        {
            if (UsesGitHubSource && !IsPinned(EffectiveTemplateUrl, SpfxVersion))
                throw new InvalidOperationException(
                    "RequirePinnedTemplates: the GitHub template source follows a branch. Set TemplateCommit (or a /tree/<sha> TemplateUrl), or use LocalSources.");
            foreach (var r in RemoteSources)
                if (!IsPinned(r, null))
                    throw new InvalidOperationException($"RequirePinnedTemplates: remote source '{r}' isn't pinned to a /tree/<sha>.");
        }
    }

    /// <summary>The template source hosts this invocation will contact.</summary>
    public IEnumerable<string> GitHubSourceHosts()
    {
        if (UsesGitHubSource)
            yield return ParseRepoUrl(EffectiveTemplateUrl ?? DefaultTemplateRepository)?.Host ?? "github.com";
        foreach (var r in RemoteSources)
            if (ParseRepoUrl(r) is { } p) yield return p.Host;
    }

    /// <summary>Build the <see cref="CommandPlan"/>.</summary>
    public CommandPlan ToCommandPlan()
    {
        Validate();

        var resolution = Binary
            ?? SpfxCliResolver.TryResolve(ToolDirectory ?? WorkingDirectory ?? Directory.GetCurrentDirectory())
            ?? throw new InvalidOperationException(
                "The SharePoint Framework CLI was not found: no @microsoft/spfx-cli package in node_modules at the tool directory " +
                "or any parent, and no spfx on PATH. Install it into a tool directory with a lockfile " +
                "(npm install --save-exact @microsoft/spfx-cli@<version>) and point ToolDirectory at it.");

        var args = new List<string>(resolution.PrefixArguments) { Command };
        if (UsesGitHubSource && EffectiveTemplateUrl is { } url) { args.Add("--template-url"); args.Add(url); }
        if (UsesGitHubSource && SpfxVersion is not null) { args.Add("--spfx-version"); args.Add(SpfxVersion.Trim()); }
        foreach (var l in LocalSources) { args.Add("--local-source"); args.Add(l); }
        foreach (var r in RemoteSources) { args.Add("--remote-source"); args.Add(r.Trim()); }
        AppendArguments(args);
        args.AddRange(ExtraArguments);

        var env = new Dictionary<string, string>(EnvironmentVariables, StringComparer.Ordinal)
        {
            ["SPFX_CI_MODE"] = DeterministicIds ? "1" : "0",
            ["SPFX_TEMPLATE_REPO_URL"] = "",
            ["GITHUB_TOKEN"] = GitHubToken?.Reveal() ?? "",
        };

        return new CommandPlan
        {
            Executable = resolution.Executable,
            Arguments = args,
            Environment = env,
            WorkingDirectory = WorkingDirectory,
            Secrets = GitHubToken is null ? Array.Empty<Secret>() : new[] { GitHubToken },
        };
    }

    internal sealed record RepoUrlParts(string Host, string Owner, string Repo, string? Ref);

    internal static RepoUrlParts? ParseRepoUrl(string? url)
    {
        if (url is null) return null;
        var m = RepoUrl.Match(url.Trim());
        if (!m.Success) return null;
        return new RepoUrlParts(
            m.Groups["host"].Value.ToLowerInvariant(),
            m.Groups["owner"].Value,
            m.Groups["repo"].Value,
            m.Groups["ref"].Success ? m.Groups["ref"].Value : null);
    }

    internal static bool IsPinned(string? url, string? spfxVersion)
    {
        if (spfxVersion is not null) return false;
        var parts = ParseRepoUrl(url);
        return parts?.Ref is { } r && CommitSha.IsMatch(r);
    }
}

/// <summary>Settings for <c>spfx create</c>: scaffold a new solution, or add a component to an existing one.</summary>
public sealed class SpfxCreateSettings : SpfxCliSettingsBase
{
    private static readonly Regex SolutionNamePattern = new("^[a-zA-Z0-9][a-zA-Z0-9-_]*$", RegexOptions.CultureInvariant);

    /// <inheritdoc />
    protected override string Command => "create";

    /// <inheritdoc />
    protected override bool UsesGitHubSource => LocalSources.Count == 0;

    /// <summary><c>--template</c> (required): the template name, for example <c>webpart-react</c>.</summary>
    public string? Template { get; set; }

    /// <summary><c>--library-name</c> (required): the npm package name of the solution.</summary>
    public string? LibraryName { get; set; }

    /// <summary><c>--component-name</c> (required): for example <c>Hello World</c>.</summary>
    public string? ComponentName { get; set; }

    /// <summary><c>--component-alias</c>. Defaults to the component name.</summary>
    public string? ComponentAlias { get; set; }

    /// <summary><c>--component-description</c>.</summary>
    public string? ComponentDescription { get; set; }

    /// <summary><c>--solution-name</c>: letters, digits, <c>-</c> and <c>_</c>, starting with a letter or digit. Defaults to the hyphen-case component name.</summary>
    public string? SolutionName { get; set; }

    /// <summary>
    /// <c>--target-dir</c>. When it already holds a scaffolded solution, the component is added to it and the
    /// shared config files are merged. Without it the CLI writes to <c>&lt;cwd&gt;/&lt;solution name&gt;</c>.
    /// </summary>
    public string? TargetDirectory { get; set; }

    /// <summary><c>--package-manager</c>. Always emitted. Default <see cref="SpfxPackageManager.None"/>.</summary>
    public SpfxPackageManager PackageManager { get; set; } = SpfxPackageManager.None;

    /// <inheritdoc />
    protected override void Validate()
    {
        if (string.IsNullOrWhiteSpace(Template)) throw new InvalidOperationException("Template is required (spfx create --template).");
        if (string.IsNullOrWhiteSpace(LibraryName)) throw new InvalidOperationException("LibraryName is required (spfx create --library-name).");
        if (string.IsNullOrWhiteSpace(ComponentName)) throw new InvalidOperationException("ComponentName is required and can't be only whitespace.");
        if (SolutionName is not null && !SolutionNamePattern.IsMatch(SolutionName.Trim()))
            throw new InvalidOperationException($"SolutionName '{SolutionName}' must contain only letters, digits, '-' and '_', and start with a letter or digit.");
        if (LocalSources.Count > 0 && (SpfxVersion is not null || TemplateUrl is not null || TemplateCommit is not null))
            throw new InvalidOperationException(
                "spfx create ignores TemplateUrl, TemplateCommit and SpfxVersion when LocalSources are given. Set one kind of source.");
        base.Validate();
    }

    /// <inheritdoc />
    protected override void AppendArguments(List<string> args)
    {
        args.Add("--template"); args.Add(Template!.Trim());
        args.Add("--library-name"); args.Add(LibraryName!.Trim());
        args.Add("--component-name"); args.Add(ComponentName!);
        if (ComponentAlias is not null) { args.Add("--component-alias"); args.Add(ComponentAlias); }
        if (ComponentDescription is not null) { args.Add("--component-description"); args.Add(ComponentDescription); }
        if (SolutionName is not null) { args.Add("--solution-name"); args.Add(SolutionName.Trim()); }
        if (TargetDirectory is not null) { args.Add("--target-dir"); args.Add(TargetDirectory); }
        args.Add("--package-manager");
        args.Add(PackageManager switch
        {
            SpfxPackageManager.None => "none",
            SpfxPackageManager.Npm => "npm",
            SpfxPackageManager.Pnpm => "pnpm",
            SpfxPackageManager.Yarn => "yarn",
            _ => throw new InvalidOperationException($"Unknown package manager {PackageManager}."),
        });
    }
}

/// <summary>Settings for <c>spfx list-templates</c>. Its output is a table for people; for machine use read a vendored source with <see cref="SpfxTemplateCatalog"/>.</summary>
public sealed class SpfxListTemplatesSettings : SpfxCliSettingsBase
{
    /// <inheritdoc />
    protected override string Command => "list-templates";

    /// <inheritdoc />
    protected override bool UsesGitHubSource => true;

    /// <inheritdoc />
    protected override void AppendArguments(List<string> args) { }
}
