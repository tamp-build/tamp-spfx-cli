using System.Text.Json;

namespace Tamp.SpfxCli;

/// <summary>What happened to one file when <c>spfx create</c> wrote a template.</summary>
public enum SpfxFileWriteOutcome
{
    /// <summary>An outcome this version of the reader doesn't know.</summary>
    Unknown,

    /// <summary>Written as-is: the file didn't exist (or a binary file changed).</summary>
    New,

    /// <summary>Existed and was merged by one of the CLI's merge helpers (<c>package.json</c>, <c>config/*.json</c>).</summary>
    Merged,

    /// <summary>
    /// Existed with different content and has no merge helper, so the existing file was <em>kept</em> and the
    /// template's version was discarded. The scaffold is then not what the template says.
    /// </summary>
    Preserved,

    /// <summary>Existed with identical content.</summary>
    Unchanged,
}

/// <summary>One line of <c>.spfx-scaffold.jsonl</c>.</summary>
public abstract record SpfxScaffoldEvent
{
    /// <summary>The event's <c>kind</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>When it happened, if the line carried a parseable timestamp.</summary>
    public DateTimeOffset? Timestamp { get; init; }
}

/// <summary><c>session-started</c>: the start of one <c>spfx create</c> run.</summary>
public sealed record SpfxSessionStartedEvent : SpfxScaffoldEvent
{
    /// <summary>The CLI version that ran.</summary>
    public string? CliVersion { get; init; }
}

/// <summary><c>template-rendered</c>: which template produced the files, and with what context.</summary>
public sealed record SpfxTemplateRenderedEvent : SpfxScaffoldEvent
{
    /// <summary>Template name.</summary>
    public string? TemplateName { get; init; }

    /// <summary>The template's own version.</summary>
    public string? TemplateVersion { get; init; }

    /// <summary>The SPFx version the template targets.</summary>
    public string? SpfxVersion { get; init; }

    /// <summary>The CLI version that rendered it.</summary>
    public string? CliVersion { get; init; }

    /// <summary>
    /// The render context (<c>solution_name</c>, <c>libraryName</c>, <c>componentId</c>, <c>solutionId</c>, …). When a
    /// component is added to an existing solution, this run's <c>solutionId</c> is a fresh one that the
    /// merge into <c>package-solution.json</c> discards; the solution keeps its original id.
    /// </summary>
    public IReadOnlyDictionary<string, string> Context { get; init; } = new Dictionary<string, string>();
}

/// <summary><c>package-manager-selected</c>.</summary>
public sealed record SpfxPackageManagerSelectedEvent : SpfxScaffoldEvent
{
    /// <summary>The package manager that will install.</summary>
    public string? PackageManager { get; init; }

    /// <summary>The scaffold target.</summary>
    public string? TargetDirectory { get; init; }
}

/// <summary><c>file-write</c>.</summary>
public sealed record SpfxFileWriteEvent : SpfxScaffoldEvent
{
    /// <summary>Path relative to the target directory, with forward slashes.</summary>
    public string? RelativePath { get; init; }

    /// <summary>What happened to the file.</summary>
    public SpfxFileWriteOutcome Outcome { get; init; }

    /// <summary>The raw outcome string, as written.</summary>
    public string? RawOutcome { get; init; }

    /// <summary>The merge helper, when <see cref="Outcome"/> is <see cref="SpfxFileWriteOutcome.Merged"/>.</summary>
    public string? MergeHelper { get; init; }
}

/// <summary><c>package-manager-install-completed</c>.</summary>
public sealed record SpfxInstallCompletedEvent : SpfxScaffoldEvent
{
    /// <summary>The package manager that ran.</summary>
    public string? PackageManager { get; init; }

    /// <summary>Its exit code (-1 when it was killed).</summary>
    public int? ExitCode { get; init; }

    /// <summary>The signal that ended it, if any.</summary>
    public string? Signal { get; init; }
}

/// <summary>An event kind this version of the reader doesn't model (the log was written by a newer CLI).</summary>
public sealed record SpfxUnrecognizedEvent : SpfxScaffoldEvent
{
    /// <summary>The line as written.</summary>
    public required string RawJson { get; init; }
}

/// <summary>One <c>spfx create</c> run: a <c>session-started</c> and everything after it up to the next one.</summary>
public sealed record SpfxScaffoldSession
{
    /// <summary>The session's events, starting with its <c>session-started</c>.</summary>
    public required IReadOnlyList<SpfxScaffoldEvent> Events { get; init; }

    /// <summary>The CLI version that ran.</summary>
    public string? CliVersion => Events.OfType<SpfxSessionStartedEvent>().FirstOrDefault()?.CliVersion;

    /// <summary>When it started.</summary>
    public DateTimeOffset? StartedAt => Events.Count > 0 ? Events[0].Timestamp : null;

    /// <summary>The template that was rendered, or null.</summary>
    public SpfxTemplateRenderedEvent? Template => Events.OfType<SpfxTemplateRenderedEvent>().FirstOrDefault();

    /// <summary>Every file the session wrote, merged, preserved or left unchanged.</summary>
    public IReadOnlyList<SpfxFileWriteEvent> Files => Events.OfType<SpfxFileWriteEvent>().ToList();

    /// <summary>Files that kept their existing content instead of the template's.</summary>
    public IReadOnlyList<SpfxFileWriteEvent> PreservedFiles => Files.Where(f => f.Outcome == SpfxFileWriteOutcome.Preserved).ToList();

    /// <summary>The package manager selection, or null when none was run.</summary>
    public SpfxPackageManagerSelectedEvent? PackageManager => Events.OfType<SpfxPackageManagerSelectedEvent>().FirstOrDefault();

    /// <summary>The install outcome, or null when no install ran.</summary>
    public SpfxInstallCompletedEvent? Install => Events.OfType<SpfxInstallCompletedEvent>().FirstOrDefault();
}

/// <summary>
/// Reader for <c>.spfx-scaffold.jsonl</c>, the provenance log <c>spfx create</c> keeps in the solution
/// root: which CLI, which template and template version, the generated ids, and what happened to every file.
/// </summary>
/// <remarks>
/// The CLI saves the log only at the end of a <em>successful</em> run. A run that fails (an unknown template,
/// a failed install) can leave files behind and no record of itself, so "the last session isn't the one I
/// just asked for" is how a failure shows up here. <see cref="SpfxScaffoldPolicy"/> checks for that.
/// </remarks>
public sealed record SpfxScaffoldLog
{
    /// <summary>The log's file name in the solution root.</summary>
    public const string FileName = ".spfx-scaffold.jsonl";

    /// <summary>Every event, in order.</summary>
    public required IReadOnlyList<SpfxScaffoldEvent> Events { get; init; }

    /// <summary>Lines that couldn't be parsed, as <c>line N: reason</c>.</summary>
    public IReadOnlyList<string> ReadProblems { get; init; } = Array.Empty<string>();

    /// <summary>The runs, oldest first. Events before the first <c>session-started</c> form their own session.</summary>
    public IReadOnlyList<SpfxScaffoldSession> Sessions
    {
        get
        {
            var sessions = new List<SpfxScaffoldSession>();
            var current = new List<SpfxScaffoldEvent>();
            foreach (var e in Events)
            {
                if (e is SpfxSessionStartedEvent && current.Count > 0)
                {
                    sessions.Add(new SpfxScaffoldSession { Events = current });
                    current = new List<SpfxScaffoldEvent>();
                }
                current.Add(e);
            }
            if (current.Count > 0) sessions.Add(new SpfxScaffoldSession { Events = current });
            return sessions;
        }
    }

    /// <summary>The most recent run, or null for an empty log.</summary>
    public SpfxScaffoldSession? LastSession => Sessions.LastOrDefault();

    /// <summary>
    /// The solution id generated when the solution was first scaffolded (the first session's render context).
    /// Later sessions render with a new id that isn't used; <c>config/package-solution.json</c> is the authority.
    /// </summary>
    public string? OriginalSolutionId =>
        Sessions.Select(s => s.Template).FirstOrDefault(t => t is not null) is { } t && t.Context.TryGetValue("solutionId", out var id) ? id : null;

    /// <summary>Read <see cref="FileName"/> from <paramref name="solutionDirectory"/>, or null when it doesn't exist.</summary>
    public static SpfxScaffoldLog? TryRead(string solutionDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(solutionDirectory);
        var path = Path.Combine(solutionDirectory, FileName);
        return File.Exists(path) ? Parse(File.ReadAllText(path)) : null;
    }

    /// <summary>Parse JSONL content. Bad lines are reported in <see cref="ReadProblems"/>, not thrown.</summary>
    public static SpfxScaffoldLog Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var events = new List<SpfxScaffoldEvent>();
        var problems = new List<string>();
        var lines = content.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (i == 0 && line.Length > 0 && line[0] == '﻿') line = line[1..];
            if (line.Trim().Length == 0) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) { problems.Add($"line {i + 1}: not a JSON object"); continue; }
                events.Add(ParseEvent(doc.RootElement, line));
            }
            catch (JsonException ex)
            {
                problems.Add($"line {i + 1}: {ex.Message}");
            }
        }
        return new SpfxScaffoldLog { Events = events, ReadProblems = problems };
    }

    private static SpfxScaffoldEvent ParseEvent(JsonElement e, string raw)
    {
        var kind = Str(e, "kind") ?? "";
        DateTimeOffset? ts = Str(e, "timestamp") is { } t && DateTimeOffset.TryParse(t, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var parsed) ? parsed : null;

        return kind switch
        {
            "session-started" => new SpfxSessionStartedEvent { Kind = kind, Timestamp = ts, CliVersion = Str(e, "cliVersion") },
            "template-rendered" => new SpfxTemplateRenderedEvent
            {
                Kind = kind, Timestamp = ts,
                TemplateName = Str(e, "templateName"),
                TemplateVersion = Str(e, "templateVersion"),
                SpfxVersion = Str(e, "spfxVersion"),
                CliVersion = Str(e, "cliVersion"),
                Context = StringMap(e, "context"),
            },
            "package-manager-selected" => new SpfxPackageManagerSelectedEvent
            {
                Kind = kind, Timestamp = ts, PackageManager = Str(e, "packageManager"), TargetDirectory = Str(e, "targetDir"),
            },
            "file-write" => new SpfxFileWriteEvent
            {
                Kind = kind, Timestamp = ts,
                RelativePath = Str(e, "relativePath"),
                RawOutcome = Str(e, "outcome"),
                Outcome = Str(e, "outcome") switch
                {
                    "new" => SpfxFileWriteOutcome.New,
                    "merged" => SpfxFileWriteOutcome.Merged,
                    "preserved" => SpfxFileWriteOutcome.Preserved,
                    "unchanged" => SpfxFileWriteOutcome.Unchanged,
                    _ => SpfxFileWriteOutcome.Unknown,
                },
                MergeHelper = Str(e, "mergeHelper"),
            },
            "package-manager-install-completed" => new SpfxInstallCompletedEvent
            {
                Kind = kind, Timestamp = ts,
                PackageManager = Str(e, "packageManager"),
                ExitCode = e.TryGetProperty("exitCode", out var code) && code.ValueKind == JsonValueKind.Number && code.TryGetInt32(out var c) ? c : null,
                Signal = Str(e, "signal"),
            },
            _ => new SpfxUnrecognizedEvent { Kind = kind, Timestamp = ts, RawJson = raw },
        };
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static Dictionary<string, string> StringMap(JsonElement e, string name)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (e.TryGetProperty(name, out var obj) && obj.ValueKind == JsonValueKind.Object)
            foreach (var p in obj.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.String) map[p.Name] = p.Value.GetString()!;
        return map;
    }
}
