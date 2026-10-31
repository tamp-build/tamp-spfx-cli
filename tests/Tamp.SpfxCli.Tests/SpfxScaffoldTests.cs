using Xunit;

namespace Tamp.SpfxCli.Tests;

public sealed class SpfxScaffoldLogTests
{
    private static SpfxScaffoldLog Real() => SpfxScaffoldLog.Parse(Fixture.Text("two-sessions.spfx-scaffold.jsonl"));

    [Fact]
    public void Real_log_has_two_sessions()
    {
        var log = Real();
        Assert.Empty(log.ReadProblems);
        Assert.Equal(2, log.Sessions.Count);
        Assert.All(log.Sessions, s => Assert.Equal("0.1.0-pre.3", s.CliVersion));
    }

    [Fact]
    public void First_session_rendered_the_web_part()
    {
        var first = Real().Sessions[0];
        Assert.Equal("webpart-minimal", first.Template!.TemplateName);
        Assert.Equal("0.0.1", first.Template.TemplateVersion);
        Assert.Equal("1.22.2", first.Template.SpfxVersion);
        Assert.Equal("contoso-hello", first.Template.Context["libraryName"]);
        Assert.All(first.Files, f => Assert.Equal(SpfxFileWriteOutcome.New, f.Outcome));
        Assert.Null(first.Install);
        Assert.NotNull(first.StartedAt);
    }

    [Fact]
    public void Second_session_added_the_extension_and_kept_six_files()
    {
        var last = Real().LastSession!;
        Assert.Equal("extension-application-customizer", last.Template!.TemplateName);
        Assert.Equal(6, last.PreservedFiles.Count);
        Assert.Contains(last.PreservedFiles, f => f.RelativePath == "README.md");
        var merged = last.Files.Where(f => f.Outcome == SpfxFileWriteOutcome.Merged).ToList();
        Assert.Equal(4, merged.Count);
        Assert.Contains(merged, f => f.MergeHelper == "config/package-solution.json");
    }

    [Fact]
    public void Original_solution_id_comes_from_the_first_session_and_matches_the_project()
    {
        var log = Real();
        var projectId = SpfxScaffoldPolicy.ReadSolutionId(Fixture.Text("package-solution.json"));
        Assert.Equal(projectId, log.OriginalSolutionId);
        // The second render generated a different, unused id.
        Assert.NotEqual(projectId, log.LastSession!.Template!.Context["solutionId"]);
    }

    [Fact]
    public void Bad_lines_are_reported_not_thrown()
    {
        var log = SpfxScaffoldLog.Parse("{\"kind\":\"session-started\",\"cliVersion\":\"1\"}\nnot json\n[1]\n\n");
        Assert.Single(log.Events);
        Assert.Equal(2, log.ReadProblems.Count);
        Assert.StartsWith("line 2:", log.ReadProblems[0]);
    }

    [Fact]
    public void Unknown_kinds_and_outcomes_are_kept()
    {
        var log = SpfxScaffoldLog.Parse(
            "﻿{\"kind\":\"session-started\"}\r\n{\"kind\":\"future-thing\",\"x\":1}\r\n{\"kind\":\"file-write\",\"relativePath\":\"a\",\"outcome\":\"renamed\"}");
        Assert.IsType<SpfxUnrecognizedEvent>(log.Events[1]);
        var fw = Assert.IsType<SpfxFileWriteEvent>(log.Events[2]);
        Assert.Equal(SpfxFileWriteOutcome.Unknown, fw.Outcome);
        Assert.Equal("renamed", fw.RawOutcome);
    }

    [Fact]
    public void Install_event_is_read()
    {
        var log = SpfxScaffoldLog.Parse(
            "{\"kind\":\"session-started\"}\n{\"kind\":\"package-manager-selected\",\"packageManager\":\"npm\",\"targetDir\":\"/x\"}\n" +
            "{\"kind\":\"package-manager-install-completed\",\"packageManager\":\"npm\",\"exitCode\":1}");
        var s = log.LastSession!;
        Assert.Equal("npm", s.PackageManager!.PackageManager);
        Assert.Equal(1, s.Install!.ExitCode);
    }

    [Fact]
    public void Events_before_any_session_start_form_their_own_session()
    {
        var log = SpfxScaffoldLog.Parse("{\"kind\":\"file-write\",\"relativePath\":\"a\",\"outcome\":\"new\"}\n{\"kind\":\"session-started\"}");
        Assert.Equal(2, log.Sessions.Count);
    }

    [Fact]
    public void TryRead_returns_null_without_a_log()
    {
        using var t = new TempDir();
        Assert.Null(SpfxScaffoldLog.TryRead(t.Root));
    }
}

public sealed class SpfxScaffoldPolicyTests
{
    private static SpfxScaffoldLog Real() => SpfxScaffoldLog.Parse(Fixture.Text("two-sessions.spfx-scaffold.jsonl"));
    private static string PackageSolution() => Fixture.Text("package-solution.json");

    [Fact]
    public void Matching_expectations_pass_with_preserved_file_warnings()
    {
        var result = new SpfxScaffoldPolicy
        {
            StartedAfter = DateTimeOffset.Parse("2026-10-31T23:47:09Z", System.Globalization.CultureInfo.InvariantCulture),
            ExpectedCliVersion = "0.1.0-pre.3",
            ExpectedTemplate = "extension-application-customizer",
            ExpectedSpfxVersion = "1.22",
            ExpectedComponentAlias = "Banner",
        }.Evaluate(Real(), PackageSolution());

        Assert.True(result.Passed, string.Join("\n", result.Errors));
        Assert.Equal(6, result.Findings.Count(f => f.RuleId == SpfxScaffoldRules.PreservedFile && f.Severity == SpfxScaffoldSeverity.Warning));
        Assert.Equal("8df00e32-5d8f-4f23-a5d1-19748ab1cd04", result.SolutionId);
        result.ThrowIfFailed();
    }

    [Fact]
    public void A_stale_last_session_means_the_run_failed()
    {
        var result = new SpfxScaffoldPolicy
        {
            StartedAfter = DateTimeOffset.Parse("2026-11-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture),
        }.Evaluate(Real(), PackageSolution());

        Assert.Contains(result.Errors, f => f.RuleId == SpfxScaffoldRules.StaleSession);
        var ex = Assert.Throws<SpfxScaffoldPolicyException>(result.ThrowIfFailed);
        Assert.Contains("SPFXCLI004", ex.Message);
    }

    [Fact]
    public void Mismatches_are_errors()
    {
        var result = new SpfxScaffoldPolicy
        {
            ExpectedCliVersion = "0.1.0",
            ExpectedTemplate = "webpart-react",
            ExpectedTemplateVersion = "9.9.9",
            ExpectedSpfxVersion = "1.24",
            ExpectedComponentAlias = "Other",
        }.Evaluate(Real(), PackageSolution());

        var rules = result.Errors.Select(e => e.RuleId).ToHashSet();
        Assert.Superset(new HashSet<string>
        {
            SpfxScaffoldRules.CliVersion, SpfxScaffoldRules.Template, SpfxScaffoldRules.TemplateVersion,
            SpfxScaffoldRules.SpfxVersion, SpfxScaffoldRules.ComponentAlias,
        }, rules);
    }

    [Fact]
    public void Preserved_files_can_be_made_errors()
    {
        var result = new SpfxScaffoldPolicy { PreservedFileSeverity = SpfxScaffoldSeverity.Error }.Evaluate(Real(), PackageSolution());
        Assert.False(result.Passed);
        Assert.Contains(result.Errors, f => f.Subject == "file:README.md");
    }

    [Fact]
    public void Missing_log_is_an_error()
    {
        using var t = new TempDir();
        var result = new SpfxScaffoldPolicy().Evaluate(t.Root);
        Assert.Single(result.Errors, f => f.RuleId == SpfxScaffoldRules.NoSession);
    }

    [Fact]
    public void The_deterministic_fake_solution_id_is_an_error_unless_allowed()
    {
        var ps = PackageSolution().Replace("8df00e32-5d8f-4f23-a5d1-19748ab1cd04", SpfxCliSettingsBase.DeterministicSolutionId, StringComparison.Ordinal);

        Assert.Contains(new SpfxScaffoldPolicy().Evaluate(Real(), ps).Errors, f => f.RuleId == SpfxScaffoldRules.DeterministicSolutionId);
        Assert.True(new SpfxScaffoldPolicy { AllowDeterministicSolutionId = true }.Evaluate(Real(), ps).Passed);
    }

    [Fact]
    public void The_deterministic_id_in_the_render_context_is_caught_without_package_solution()
    {
        var log = SpfxScaffoldLog.Parse(
            "{\"kind\":\"session-started\",\"timestamp\":\"2026-10-31T00:00:00Z\"}\n" +
            "{\"kind\":\"template-rendered\",\"templateName\":\"t\",\"context\":{\"solutionId\":\"22222222-2222-2222-2222-222222222222\"}}");
        Assert.Contains(new SpfxScaffoldPolicy().Evaluate(log, null).Errors, f => f.RuleId == SpfxScaffoldRules.DeterministicSolutionId);
    }

    [Fact]
    public void Failed_install_session_without_template_and_unknown_events()
    {
        var log = SpfxScaffoldLog.Parse(
            "{\"kind\":\"session-started\"}\n{\"kind\":\"future-thing\"}\n" +
            "{\"kind\":\"package-manager-install-completed\",\"packageManager\":\"npm\",\"exitCode\":0,\"signal\":\"SIGTERM\"}\nbroken");
        var result = new SpfxScaffoldPolicy().Evaluate(log, null);
        var rules = result.Findings.Select(f => f.RuleId).ToList();
        Assert.Contains(SpfxScaffoldRules.InstallFailed, rules);
        Assert.Contains(SpfxScaffoldRules.NoTemplate, rules);
        Assert.Contains(SpfxScaffoldRules.UnreadableLog, rules);
        Assert.Contains(result.Findings, f => f.RuleId == SpfxScaffoldRules.UnrecognizedEvent && f.Severity == SpfxScaffoldSeverity.Note);
    }

    [Fact]
    public void Evaluate_reads_from_a_solution_directory()
    {
        using var t = new TempDir();
        File.Copy(Fixture.Path("two-sessions.spfx-scaffold.jsonl"), Path.Combine(t.Root, SpfxScaffoldLog.FileName));
        File.Copy(Fixture.Path("package-solution.json"), Path.Combine(t.Dir("config"), "package-solution.json"));
        var result = new SpfxScaffoldPolicy { ExpectedTemplate = "extension-application-customizer" }.Evaluate(t.Root);
        Assert.True(result.Passed);
        Assert.Equal("8df00e32-5d8f-4f23-a5d1-19748ab1cd04", result.SolutionId);
    }

    [Theory]
    [InlineData("1.22", "1.22.2", true)]
    [InlineData("1.22.2", "1.22.2", true)]
    [InlineData("1.2", "1.22.2", false)]
    [InlineData("1.22", null, false)]
    public void Spfx_version_prefix_matches_on_a_dot_boundary(string expected, string? actual, bool matches)
    {
        Assert.Equal(matches, SpfxScaffoldPolicy.VersionMatches(expected, actual));
    }

    [Theory]
    [InlineData("{ \"solution\": { \"id\": \"abc\" } }", "abc")]
    [InlineData("// comment\n{ \"solution\": { \"id\": \"abc\", }, }", "abc")]
    [InlineData("{ \"solution\": [] }", null)]
    [InlineData("not json", null)]
    public void Solution_id_reading(string json, string? id)
    {
        Assert.Equal(id, SpfxScaffoldPolicy.ReadSolutionId(json));
    }
}

public sealed class SpfxTemplateCatalogTests
{
    [Fact]
    public void Reads_immediate_template_folders_like_the_cli()
    {
        var catalog = SpfxTemplateCatalog.Load(Fixture.Path("templates"));

        Assert.Equal(new[] { "future-template", "webpart-minimal" }, catalog.Templates.Select(t => t.Name));
        Assert.Single(catalog.Problems);
        Assert.StartsWith("broken:", catalog.Problems[0]);
    }

    [Fact]
    public void Real_template_json_fields_are_read_and_unknown_fields_reported()
    {
        var t = SpfxTemplateCatalog.Load(Fixture.Path("templates")).Find("webpart-minimal")!;
        Assert.Equal("webpart", t.Category);
        Assert.Equal("0.0.1", t.Version);
        Assert.Equal("1.22.2", t.SpfxVersion);
        Assert.Null(t.MinimumEngineVersion);
        Assert.Equal(new[] { "contextSchema" }, t.UnknownFields);
    }

    [Fact]
    public void Engine_check_finds_templates_needing_a_newer_cli()
    {
        var catalog = SpfxTemplateCatalog.Load(Fixture.Path("templates"));
        Assert.Equal(new[] { "future-template" }, catalog.RequiringNewerEngine("0.1.0-pre.3").Select(t => t.Name));
        Assert.Empty(catalog.RequiringNewerEngine("0.2.0"));
        Assert.Throws<ArgumentException>(() => catalog.RequiringNewerEngine("latest"));
    }

    [Fact]
    public void Missing_source_throws()
    {
        Assert.Throws<DirectoryNotFoundException>(() => SpfxTemplateCatalog.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
    }

    [Fact]
    public void Find_returns_null_for_unknown()
    {
        Assert.Null(SpfxTemplateCatalog.Load(Fixture.Path("templates")).Find("nope"));
    }
}

public sealed class SemVerTests
{
    [Theory]
    [InlineData("0.1.0-pre.3", "0.1.0-pre.10")]
    [InlineData("0.1.0-pre.10", "0.1.0")]
    [InlineData("0.1.0-alpha", "0.1.0-alpha.1")]
    [InlineData("0.1.0-alpha.1", "0.1.0-alpha.beta")]
    [InlineData("0.1.0-beta", "0.1.0-rc")]
    [InlineData("0.9.9", "0.10.0")]
    [InlineData("1.0.0", "2.0.0")]
    [InlineData("1.0.0", "1.0.1")]
    public void Precedence(string lower, string higher)
    {
        var a = SemVer.TryParse(lower)!;
        var b = SemVer.TryParse(higher)!;
        Assert.True(a < b);
        Assert.True(b > a);
        Assert.True(a <= b);
        Assert.True(b >= a);
    }

    [Fact]
    public void Metadata_is_ignored_for_ordering_and_kept_in_text()
    {
        var a = SemVer.TryParse("v1.2.3-rc.1+build.5")!;
        Assert.Equal(0, a.CompareTo(SemVer.TryParse("1.2.3-rc.1")));
        Assert.Equal("1.2.3-rc.1+build.5", a.ToString());
        Assert.Equal(1, a.CompareTo(null));
    }

    [Theory]
    [InlineData("1.2")]
    [InlineData("01.2.3")]
    [InlineData("1.2.3-")]
    [InlineData(null)]
    public void Invalid_versions(string? v)
    {
        Assert.Null(SemVer.TryParse(v));
    }
}
