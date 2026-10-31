using Xunit;

namespace Tamp.SpfxCli.Tests;

public sealed class SpfxCliTests
{
    private const string Sha = "5753ddac6c261c5023d9218cd9bbac7640d403b7";

    private static readonly SpfxCliResolution Bin = new()
    {
        Executable = "node",
        PrefixArguments = new[] { "/cli/bin/spfx" },
        Source = SpfxCliResolutionSource.Explicit,
    };

    private static SpfxCreateSettings Hello(SpfxCreateSettings s) => s
        .SetBinary(Bin)
        .SetTemplate("webpart-minimal")
        .SetLibraryName("contoso-hello")
        .SetComponentName("Hello Tamp");

    [Fact]
    public void Create_emits_required_flags_and_an_explicit_package_manager()
    {
        var plan = SpfxCli.Create(s => Hello(s));

        Assert.Equal("node", plan.Executable);
        Assert.Equal(new[]
        {
            "/cli/bin/spfx", "create",
            "--template", "webpart-minimal",
            "--library-name", "contoso-hello",
            "--component-name", "Hello Tamp",
            "--package-manager", "none",
        }, plan.Arguments);
    }

    [Fact]
    public void Create_emits_every_optional_flag()
    {
        var plan = SpfxCli.Create(s => Hello(s)
            .SetComponentAlias("HelloAlias")
            .SetComponentDescription("Says hello")
            .SetSolutionName("hello_solution-1")
            .SetTargetDirectory("/work/hello")
            .SetPackageManager(SpfxPackageManager.Pnpm)
            .AddArgument("--future-flag"));

        Assert.Equal(new[]
        {
            "/cli/bin/spfx", "create",
            "--template", "webpart-minimal",
            "--library-name", "contoso-hello",
            "--component-name", "Hello Tamp",
            "--component-alias", "HelloAlias",
            "--component-description", "Says hello",
            "--solution-name", "hello_solution-1",
            "--target-dir", "/work/hello",
            "--package-manager", "pnpm",
            "--future-flag",
        }, plan.Arguments);
    }

    [Fact]
    public void Template_commit_pins_the_default_repository()
    {
        var plan = SpfxCli.Create(s => Hello(s).SetTemplateCommit(Sha));
        AssertPair(plan, "--template-url", $"https://github.com/SharePoint/spfx/tree/{Sha}");
        Assert.DoesNotContain("--spfx-version", plan.Arguments);
    }

    [Fact]
    public void Template_commit_pins_a_custom_repository_and_drops_a_git_suffix()
    {
        var plan = SpfxCli.Create(s => Hello(s).SetTemplateUrl("https://github.contoso.com/web/spfx-templates.git/").SetTemplateCommit(Sha));
        AssertPair(plan, "--template-url", $"https://github.contoso.com/web/spfx-templates/tree/{Sha}");
    }

    [Fact]
    public void Template_commit_with_spfx_version_is_refused_because_the_cli_lets_the_branch_win()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => SpfxCli.Create(s => Hello(s).SetTemplateCommit(Sha).SetSpfxVersion("1.22")));
        Assert.Contains("override", ex.Message);
    }

    [Fact]
    public void Template_commit_with_a_tree_url_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() => SpfxCli.Create(s => Hello(s)
            .SetTemplateUrl("https://github.com/SharePoint/spfx/tree/main").SetTemplateCommit(Sha)));
    }

    [Theory]
    [InlineData("5753ddac")]
    [InlineData("version/latest")]
    [InlineData("zz53ddac6c261c5023d9218cd9bbac7640d403b7")]
    public void Template_commit_must_be_a_full_sha(string sha)
    {
        Assert.Throws<InvalidOperationException>(() => SpfxCli.Create(s => Hello(s).SetTemplateCommit(sha)));
    }

    [Fact]
    public void Spfx_version_is_emitted()
    {
        var plan = SpfxCli.Create(s => Hello(s).SetSpfxVersion("next"));
        AssertPair(plan, "--spfx-version", "next");
    }

    [Theory]
    [InlineData("1.22 ; rm")]
    [InlineData(" ")]
    public void Bad_spfx_version_is_refused(string version)
    {
        Assert.Throws<InvalidOperationException>(() => SpfxCli.Create(s => Hello(s).SetSpfxVersion(version)));
    }

    [Theory]
    [InlineData("http://github.com/SharePoint/spfx")]
    [InlineData("github.com/SharePoint/spfx")]
    [InlineData("https://github.com/SharePoint")]
    public void Template_url_must_be_an_https_repository(string url)
    {
        Assert.Throws<InvalidOperationException>(() => SpfxCli.Create(s => Hello(s).SetTemplateUrl(url)));
    }

    [Fact]
    public void Local_sources_replace_the_github_source_for_create()
    {
        var plan = SpfxCli.Create(s => Hello(s).AddLocalSource("/vendor/spfx/templates"));
        AssertPair(plan, "--local-source", "/vendor/spfx/templates");
        Assert.DoesNotContain("--template-url", plan.Arguments);
    }

    [Fact]
    public void Create_refuses_github_settings_that_local_sources_would_silently_override()
    {
        Assert.Throws<InvalidOperationException>(() => SpfxCli.Create(s => Hello(s).AddLocalSource("/t").SetSpfxVersion("1.22")));
        Assert.Throws<InvalidOperationException>(() => SpfxCli.Create(s => Hello(s).AddLocalSource("/t").SetTemplateCommit(Sha)));
    }

    [Fact]
    public void Remote_sources_are_emitted()
    {
        var plan = SpfxCli.Create(s => Hello(s).AddRemoteSource($"https://github.com/contoso/templates/tree/{Sha}"));
        AssertPair(plan, "--remote-source", $"https://github.com/contoso/templates/tree/{Sha}");
    }

    [Theory]
    [InlineData(null, "", null)]
    [InlineData("/cli", "x", null)]
    public void Missing_required_values_are_refused(string? template, string library, string? component)
    {
        Assert.Throws<InvalidOperationException>(() => SpfxCli.Create(new SpfxCreateSettings
        {
            Binary = Bin, Template = template, LibraryName = library, ComponentName = component,
        }));
    }

    [Fact]
    public void Whitespace_component_name_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() => SpfxCli.Create(s => Hello(s).SetComponentName("   ")));
    }

    [Theory]
    [InlineData("-leading-dash")]
    [InlineData("has space")]
    [InlineData("dot.name")]
    public void Bad_solution_names_are_refused(string name)
    {
        Assert.Throws<InvalidOperationException>(() => SpfxCli.Create(s => Hello(s).SetSolutionName(name)));
    }

    [Fact]
    public void Environment_neutralises_ci_mode_repo_redirect_and_inherited_token()
    {
        var plan = SpfxCli.Create(s => Hello(s).SetEnvironmentVariable("SPFX_CI_MODE", "1").SetEnvironmentVariable("FOO", "bar"));

        Assert.Equal("0", plan.Environment["SPFX_CI_MODE"]);
        Assert.Equal("", plan.Environment["SPFX_TEMPLATE_REPO_URL"]);
        Assert.Equal("", plan.Environment["GITHUB_TOKEN"]);
        Assert.Equal("bar", plan.Environment["FOO"]);
        Assert.Empty(plan.Secrets);
    }

    [Fact]
    public void Deterministic_ids_are_opt_in()
    {
        var plan = SpfxCli.Create(s => Hello(s).SetDeterministicIds());
        Assert.Equal("1", plan.Environment["SPFX_CI_MODE"]);
    }

    [Fact]
    public void Github_token_goes_through_the_environment_and_is_registered_as_a_secret()
    {
        var token = new Secret("GitHub token", "ghp_secretvalue");
        var plan = SpfxCli.Create(s => Hello(s).SetGitHubToken(token));

        Assert.Equal("ghp_secretvalue", plan.Environment["GITHUB_TOKEN"]);
        Assert.Contains(token, plan.Secrets);
        Assert.DoesNotContain(plan.Arguments, a => a.Contains("ghp_secretvalue", StringComparison.Ordinal));
        Assert.DoesNotContain("--github-token", plan.Arguments);
    }

    [Fact]
    public void Github_token_is_refused_for_a_source_on_another_host()
    {
        var token = new Secret("GitHub token", "ghp_secretvalue");
        var ex = Assert.Throws<InvalidOperationException>(() => SpfxCli.Create(s => Hello(s)
            .SetGitHubToken(token)
            .AddRemoteSource("https://attacker.example/owner/repo")));
        Assert.Contains("attacker.example", ex.Message);
    }

    [Fact]
    public void Github_token_can_be_allowed_for_an_enterprise_host()
    {
        var token = new Secret("GitHub token", "ghp_secretvalue");
        var plan = SpfxCli.Create(s => Hello(s)
            .SetTemplateUrl("https://github.contoso.com/web/templates")
            .SetGitHubToken(token)
            .AddGitHubTokenHost("github.contoso.com"));
        Assert.Equal("ghp_secretvalue", plan.Environment["GITHUB_TOKEN"]);
    }

    [Fact]
    public void Github_token_with_local_only_create_contacts_no_host()
    {
        var token = new Secret("GitHub token", "ghp_secretvalue");
        var s = Hello(new SpfxCreateSettings()).AddLocalSource("/t").SetGitHubToken(token);
        Assert.Empty(s.GitHubSourceHosts());
        SpfxCli.Create(s);
    }

    [Fact]
    public void Require_pinned_refuses_a_branch_source()
    {
        Assert.Throws<InvalidOperationException>(() => SpfxCli.Create(s => Hello(s).SetRequirePinnedTemplates()));
        Assert.Throws<InvalidOperationException>(() => SpfxCli.Create(s => Hello(s).SetRequirePinnedTemplates().SetSpfxVersion("1.22")));
        Assert.Throws<InvalidOperationException>(() => SpfxCli.Create(s => Hello(s).SetRequirePinnedTemplates()
            .SetTemplateUrl("https://github.com/SharePoint/spfx/tree/version-latest")));
    }

    [Fact]
    public void Require_pinned_accepts_a_commit_a_sha_url_or_local_sources()
    {
        SpfxCli.Create(s => Hello(s).SetRequirePinnedTemplates().SetTemplateCommit(Sha));
        SpfxCli.Create(s => Hello(s).SetRequirePinnedTemplates().SetTemplateUrl($"https://github.com/SharePoint/spfx/tree/{Sha}"));
        SpfxCli.Create(s => Hello(s).SetRequirePinnedTemplates().AddLocalSource("/vendor/templates"));
    }

    [Fact]
    public void Require_pinned_checks_remote_sources_too()
    {
        Assert.Throws<InvalidOperationException>(() => SpfxCli.Create(s => Hello(s).SetRequirePinnedTemplates()
            .AddLocalSource("/vendor/templates").AddRemoteSource("https://github.com/contoso/templates")));
    }

    [Fact]
    public void List_templates_always_uses_the_github_source()
    {
        var plan = SpfxCli.ListTemplates(s => s.SetBinary(Bin).SetTemplateCommit(Sha).AddLocalSource("/t"));
        Assert.Equal("list-templates", plan.Arguments[1]);
        AssertPair(plan, "--template-url", $"https://github.com/SharePoint/spfx/tree/{Sha}");
        AssertPair(plan, "--local-source", "/t");

        Assert.Throws<InvalidOperationException>(() => SpfxCli.ListTemplates(s => s.SetBinary(Bin).SetRequirePinnedTemplates().AddLocalSource("/t")));
    }

    [Fact]
    public void Object_init_overloads_match_fluent()
    {
        var a = SpfxCli.Create(new SpfxCreateSettings { Binary = Bin, Template = "webpart-minimal", LibraryName = "contoso-hello", ComponentName = "Hello Tamp" });
        var b = SpfxCli.Create(s => Hello(s));
        Assert.Equal(b.Arguments, a.Arguments);

        var l = SpfxCli.ListTemplates(new SpfxListTemplatesSettings { Binary = Bin });
        Assert.Equal(new[] { "/cli/bin/spfx", "list-templates" }, l.Arguments);
    }

    [Fact]
    public void Working_directory_is_carried_into_the_plan()
    {
        var plan = SpfxCli.Create(s => Hello(s).SetWorkingDirectory("/work"));
        Assert.Equal("/work", plan.WorkingDirectory);
    }

    [Fact]
    public void Tool_directory_drives_resolution()
    {
        using var t = new TempDir();
        var tool = t.Dir("tool");
        t.InstallLocal(tool);
        t.InstallShim(tool);

        var plan = SpfxCli.Create(new SpfxCreateSettings
        {
            ToolDirectory = tool, WorkingDirectory = t.Dir("work"),
            Template = "webpart-minimal", LibraryName = "x", ComponentName = "Y",
        });

        Assert.Contains("create", plan.Arguments);
    }

    [Fact]
    public void Missing_cli_gives_an_actionable_error()
    {
        using var t = new TempDir();
        var s = new SpfxCreateSettings { ToolDirectory = t.Dir("none"), Template = "a", LibraryName = "b", ComponentName = "c" };
        if (SpfxCliResolver.IsAvailable(s.ToolDirectory)) return; // a global spfx on this machine
        var ex = Assert.Throws<InvalidOperationException>(() => SpfxCli.Create(s));
        Assert.Contains("ToolDirectory", ex.Message);
    }

    [Fact]
    public void Null_extra_argument_is_refused()
    {
        var s = Hello(new SpfxCreateSettings());
        s.ExtraArguments.Add(null!);
        Assert.Throws<InvalidOperationException>(() => SpfxCli.Create(s));
    }

    [Fact]
    public void Raw_passes_arguments_through()
    {
        using var t = new TempDir();
        var tool = t.Dir("tool");
        t.InstallLocal(tool);
        t.InstallShim(tool);
        var plan = SpfxCli.Raw(tool, "--help");
        Assert.Equal("--help", plan.Arguments[^1]);
    }

    [Theory]
    [InlineData("https://github.com/SharePoint/spfx/tree/" + Sha, true)]
    [InlineData("https://github.com/SharePoint/spfx/tree/main", false)]
    [InlineData("https://github.com/SharePoint/spfx", false)]
    [InlineData(null, false)]
    public void Pinned_detection(string? url, bool pinned)
    {
        Assert.Equal(pinned, SpfxCliSettingsBase.IsPinned(url, null));
    }

    private static void AssertPair(CommandPlan plan, string flag, string value)
    {
        var i = plan.Arguments.ToList().IndexOf(flag);
        Assert.True(i >= 0, $"{flag} not emitted: {string.Join(' ', plan.Arguments)}");
        Assert.Equal(value, plan.Arguments[i + 1]);
    }
}
