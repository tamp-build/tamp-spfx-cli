using System.Text.Json;

namespace Tamp.SpfxCli;

/// <summary>Finding severity.</summary>
public enum SpfxScaffoldSeverity
{
    /// <summary>Recorded for the evidence; never fails.</summary>
    Note,

    /// <summary>Worth a look; doesn't fail.</summary>
    Warning,

    /// <summary>Fails the policy.</summary>
    Error,
}

/// <summary>One policy finding.</summary>
public sealed record SpfxScaffoldFinding(string RuleId, SpfxScaffoldSeverity Severity, string Message, string? Subject = null);

/// <summary>Rule ids.</summary>
public static class SpfxScaffoldRules
{
    /// <summary>No scaffold log, or no session in it.</summary>
    public const string NoSession = "SPFXCLI001";

    /// <summary>Lines of the log couldn't be parsed.</summary>
    public const string UnreadableLog = "SPFXCLI002";

    /// <summary>The log has event kinds this reader doesn't know (written by a newer CLI).</summary>
    public const string UnrecognizedEvent = "SPFXCLI003";

    /// <summary>The last session started before <see cref="SpfxScaffoldPolicy.StartedAfter"/>: the run you just did isn't recorded, so it failed.</summary>
    public const string StaleSession = "SPFXCLI004";

    /// <summary>The last session rendered no template.</summary>
    public const string NoTemplate = "SPFXCLI005";

    /// <summary>CLI version differs from <see cref="SpfxScaffoldPolicy.ExpectedCliVersion"/>.</summary>
    public const string CliVersion = "SPFXCLI010";

    /// <summary>Template differs from <see cref="SpfxScaffoldPolicy.ExpectedTemplate"/>.</summary>
    public const string Template = "SPFXCLI011";

    /// <summary>Template version differs from <see cref="SpfxScaffoldPolicy.ExpectedTemplateVersion"/>.</summary>
    public const string TemplateVersion = "SPFXCLI012";

    /// <summary>Template's SPFx version doesn't match <see cref="SpfxScaffoldPolicy.ExpectedSpfxVersion"/>.</summary>
    public const string SpfxVersion = "SPFXCLI013";

    /// <summary>Component alias differs from <see cref="SpfxScaffoldPolicy.ExpectedComponentAlias"/>.</summary>
    public const string ComponentAlias = "SPFXCLI014";

    /// <summary>The solution carries the CLI's fixed fake id from deterministic mode.</summary>
    public const string DeterministicSolutionId = "SPFXCLI020";

    /// <summary>A file kept its existing content instead of the template's.</summary>
    public const string PreservedFile = "SPFXCLI030";

    /// <summary>The package-manager install failed.</summary>
    public const string InstallFailed = "SPFXCLI031";
}

/// <summary>The outcome of <see cref="SpfxScaffoldPolicy.Evaluate(string)"/>.</summary>
public sealed record SpfxScaffoldPolicyResult
{
    /// <summary>The session evaluated (the most recent), or null.</summary>
    public SpfxScaffoldSession? Session { get; init; }

    /// <summary>The solution id in <c>config/package-solution.json</c>, when it could be read.</summary>
    public string? SolutionId { get; init; }

    /// <summary>All findings.</summary>
    public required IReadOnlyList<SpfxScaffoldFinding> Findings { get; init; }

    /// <summary>Error findings.</summary>
    public IReadOnlyList<SpfxScaffoldFinding> Errors => Findings.Where(f => f.Severity == SpfxScaffoldSeverity.Error).ToList();

    /// <summary>True when there are no errors.</summary>
    public bool Passed => Errors.Count == 0;

    /// <summary>Throw <see cref="SpfxScaffoldPolicyException"/> when the policy failed.</summary>
    public void ThrowIfFailed()
    {
        if (!Passed) throw new SpfxScaffoldPolicyException(this);
    }
}

/// <summary>Thrown by <see cref="SpfxScaffoldPolicyResult.ThrowIfFailed"/>.</summary>
public sealed class SpfxScaffoldPolicyException : Exception
{
    /// <summary>Create from a failed result.</summary>
    public SpfxScaffoldPolicyException(SpfxScaffoldPolicyResult result)
        : base("SPFx scaffold policy failed:" + Environment.NewLine +
               string.Join(Environment.NewLine, result.Errors.Select(e => $"  {e.RuleId} {e.Message}")))
    {
        Result = result;
    }

    /// <summary>The failed result.</summary>
    public SpfxScaffoldPolicyResult Result { get; }
}

/// <summary>
/// Checks that a scaffold is what the pipeline asked for, from the CLI's own <c>.spfx-scaffold.jsonl</c> and the
/// solution's <c>config/package-solution.json</c>. Evaluates the most recent session.
/// </summary>
public sealed class SpfxScaffoldPolicy
{
    /// <summary>
    /// The run being checked started at or after this instant. Because the CLI records only successful runs,
    /// an older last session means the run failed. Set it to the time just before you ran <c>spfx create</c>.
    /// </summary>
    public DateTimeOffset? StartedAfter { get; set; }

    /// <summary>Exact CLI version, for example <c>0.1.0-pre.3</c>.</summary>
    public string? ExpectedCliVersion { get; set; }

    /// <summary>Template name.</summary>
    public string? ExpectedTemplate { get; set; }

    /// <summary>Exact template version.</summary>
    public string? ExpectedTemplateVersion { get; set; }

    /// <summary>SPFx version the template targets: exact (<c>1.22.2</c>) or a prefix on a <c>.</c> boundary (<c>1.22</c>).</summary>
    public string? ExpectedSpfxVersion { get; set; }

    /// <summary>The component alias the session rendered.</summary>
    public string? ExpectedComponentAlias { get; set; }

    /// <summary>Severity for a file that kept its existing content. Default <see cref="SpfxScaffoldSeverity.Warning"/>.</summary>
    public SpfxScaffoldSeverity PreservedFileSeverity { get; set; } = SpfxScaffoldSeverity.Warning;

    /// <summary>Allow the deterministic fake solution id (for template golden-file tests only).</summary>
    public bool AllowDeterministicSolutionId { get; set; }

    /// <summary>Evaluate the solution in <paramref name="solutionDirectory"/>.</summary>
    public SpfxScaffoldPolicyResult Evaluate(string solutionDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(solutionDirectory);
        var log = SpfxScaffoldLog.TryRead(solutionDirectory);
        var ps = Path.Combine(solutionDirectory, "config", "package-solution.json");
        return Evaluate(log, File.Exists(ps) ? File.ReadAllText(ps) : null);
    }

    /// <summary>Evaluate a parsed log and (optionally) the text of <c>config/package-solution.json</c>.</summary>
    public SpfxScaffoldPolicyResult Evaluate(SpfxScaffoldLog? log, string? packageSolutionJson)
    {
        var findings = new List<SpfxScaffoldFinding>();
        var solutionId = ReadSolutionId(packageSolutionJson);

        if (solutionId is not null && IsDeterministic(solutionId) && !AllowDeterministicSolutionId)
            findings.Add(new(SpfxScaffoldRules.DeterministicSolutionId, SpfxScaffoldSeverity.Error,
                $"config/package-solution.json has the CLI's fixed deterministic solution id {solutionId}. Every solution scaffolded " +
                "with SPFX_CI_MODE=1 shares it, so they would overwrite each other in an app catalog.", "solution:id"));

        var session = log?.LastSession;
        if (log is null || session is null)
        {
            findings.Add(new(SpfxScaffoldRules.NoSession, SpfxScaffoldSeverity.Error,
                $"No scaffold session is recorded ({SpfxScaffoldLog.FileName} is missing or empty). The CLI writes it only after a successful create.", "log"));
            return new SpfxScaffoldPolicyResult { Findings = findings, SolutionId = solutionId };
        }

        foreach (var p in log.ReadProblems)
            findings.Add(new(SpfxScaffoldRules.UnreadableLog, SpfxScaffoldSeverity.Error, $"{SpfxScaffoldLog.FileName} {p}.", "log"));

        foreach (var kind in session.Events.OfType<SpfxUnrecognizedEvent>().Select(e => e.Kind).Distinct(StringComparer.Ordinal))
            findings.Add(new(SpfxScaffoldRules.UnrecognizedEvent, SpfxScaffoldSeverity.Note,
                $"The log has '{kind}' events, which this reader doesn't model (written by a newer CLI).", $"event:{kind}"));

        if (StartedAfter is { } after && (session.StartedAt is not { } started || started < after))
            findings.Add(new(SpfxScaffoldRules.StaleSession, SpfxScaffoldSeverity.Error,
                $"The most recent session started {session.StartedAt?.ToString("O") ?? "at an unknown time"}, before {after:O}. The run being checked " +
                "isn't recorded, so it failed (the CLI saves the log only after a successful create).", "session"));

        Expect(findings, SpfxScaffoldRules.CliVersion, "CLI version", ExpectedCliVersion, session.CliVersion, "cli");

        var template = session.Template;
        if (template is null)
        {
            findings.Add(new(SpfxScaffoldRules.NoTemplate, SpfxScaffoldSeverity.Error, "The most recent session rendered no template.", "template"));
        }
        else
        {
            Expect(findings, SpfxScaffoldRules.Template, "Template", ExpectedTemplate, template.TemplateName, "template");
            Expect(findings, SpfxScaffoldRules.TemplateVersion, "Template version", ExpectedTemplateVersion, template.TemplateVersion, "template:version");
            if (ExpectedSpfxVersion is { } spfx && !VersionMatches(spfx, template.SpfxVersion))
                findings.Add(new(SpfxScaffoldRules.SpfxVersion, SpfxScaffoldSeverity.Error,
                    $"Template '{template.TemplateName}' targets SPFx {template.SpfxVersion ?? "(unknown)"}, not {spfx}.", "template:spfxVersion"));
            Expect(findings, SpfxScaffoldRules.ComponentAlias, "Component alias", ExpectedComponentAlias,
                template.Context.TryGetValue("componentAlias", out var alias) ? alias : null, "component:alias");

            if (!AllowDeterministicSolutionId && solutionId is null
                && template.Context.TryGetValue("solutionId", out var renderedId) && IsDeterministic(renderedId))
                findings.Add(new(SpfxScaffoldRules.DeterministicSolutionId, SpfxScaffoldSeverity.Error,
                    $"The session rendered with the CLI's fixed deterministic solution id {renderedId} (SPFX_CI_MODE=1).", "solution:id"));
        }

        foreach (var f in session.PreservedFiles)
            findings.Add(new(SpfxScaffoldRules.PreservedFile, PreservedFileSeverity,
                $"'{f.RelativePath}' already existed with different content and was kept; the template's version was not written.", $"file:{f.RelativePath}"));

        if (session.Install is { } install && (install.ExitCode != 0 || install.Signal is not null))
            findings.Add(new(SpfxScaffoldRules.InstallFailed, SpfxScaffoldSeverity.Error,
                $"{install.PackageManager} install failed (exit {install.ExitCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"}{(install.Signal is null ? "" : ", signal " + install.Signal)}).", "install"));

        return new SpfxScaffoldPolicyResult { Session = session, Findings = findings, SolutionId = solutionId };
    }

    private static void Expect(List<SpfxScaffoldFinding> findings, string rule, string what, string? expected, string? actual, string subject)
    {
        if (expected is null || string.Equals(expected, actual, StringComparison.Ordinal)) return;
        findings.Add(new(rule, SpfxScaffoldSeverity.Error, $"{what} is {actual ?? "(not recorded)"}, expected {expected}.", subject));
    }

    internal static bool VersionMatches(string expected, string? actual) =>
        actual is not null && (string.Equals(expected, actual, StringComparison.Ordinal)
            || actual.StartsWith(expected.TrimEnd('.') + ".", StringComparison.Ordinal));

    private static bool IsDeterministic(string id) =>
        string.Equals(id.Trim().Trim('{', '}'), SpfxCliSettingsBase.DeterministicSolutionId, StringComparison.OrdinalIgnoreCase);

    internal static string? ReadSolutionId(string? packageSolutionJson)
    {
        if (packageSolutionJson is null) return null;
        try
        {
            using var doc = JsonDocument.Parse(packageSolutionJson.TrimStart('﻿'),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("solution", out var s) && s.ValueKind == JsonValueKind.Object
                && s.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String ? id.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
