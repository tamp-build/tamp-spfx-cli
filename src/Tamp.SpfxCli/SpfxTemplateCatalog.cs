using System.Text.Json;

namespace Tamp.SpfxCli;

/// <summary>One template's <c>template.json</c>.</summary>
public sealed record SpfxTemplateInfo
{
    /// <summary>Template name (what <c>--template</c> takes).</summary>
    public required string Name { get; init; }

    /// <summary><c>webpart</c>, <c>extension</c>, <c>ace</c> or <c>library</c>.</summary>
    public string? Category { get; init; }

    /// <summary>Description.</summary>
    public string? Description { get; init; }

    /// <summary>The template's own version.</summary>
    public string? Version { get; init; }

    /// <summary>The SPFx version it scaffolds.</summary>
    public string? SpfxVersion { get; init; }

    /// <summary>The lowest template engine (CLI) version that can render it, if declared.</summary>
    public string? MinimumEngineVersion { get; init; }

    /// <summary>The template folder.</summary>
    public required string Directory { get; init; }

    /// <summary>
    /// Top-level <c>template.json</c> fields this reader doesn't model. The CLI warns about fields <em>it</em>
    /// doesn't know, which is how a template repository ahead of the installed CLI shows up.
    /// </summary>
    public IReadOnlyList<string> UnknownFields { get; init; } = Array.Empty<string>();
}

/// <summary>
/// The templates in a local template source, read the way <c>spfx create --local-source</c> reads them: each
/// immediate, non-dot subdirectory that holds a <c>template.json</c>. For a checkout of the SharePoint/spfx
/// repository that's its <c>templates/</c> folder.
/// </summary>
/// <remarks>
/// <c>spfx list-templates</c> prints a table for people. On a build machine, vendor the templates (a reviewed
/// checkout at a known commit), read them with this catalog, check the template you need is there and that
/// the installed CLI can render it, and then scaffold with <see cref="SpfxCliSettingsBase.LocalSources"/>.
/// </remarks>
public sealed record SpfxTemplateCatalog
{
    private static readonly HashSet<string> KnownFields = new(StringComparer.Ordinal)
    {
        "$schema", "name", "category", "description", "version", "spfxVersion", "minimumEngineVersion",
    };

    /// <summary>The source folder.</summary>
    public required string SourceDirectory { get; init; }

    /// <summary>Templates, ordered by name.</summary>
    public required IReadOnlyList<SpfxTemplateInfo> Templates { get; init; }

    /// <summary>Folders whose <c>template.json</c> couldn't be read, as <c>folder: reason</c>.</summary>
    public IReadOnlyList<string> Problems { get; init; } = Array.Empty<string>();

    /// <summary>Read a local template source.</summary>
    public static SpfxTemplateCatalog Load(string sourceDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceDirectory);
        var root = Path.GetFullPath(sourceDirectory);
        if (!System.IO.Directory.Exists(root))
            throw new DirectoryNotFoundException($"Template source '{root}' does not exist.");

        var templates = new List<SpfxTemplateInfo>();
        var problems = new List<string>();
        foreach (var dir in System.IO.Directory.EnumerateDirectories(root))
        {
            var name = Path.GetFileName(dir);
            if (name.StartsWith('.')) continue;
            var file = Path.Combine(dir, "template.json");
            if (!File.Exists(file)) continue;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                var r = doc.RootElement;
                if (r.ValueKind != JsonValueKind.Object) { problems.Add($"{name}: template.json is not an object"); continue; }
                var templateName = Str(r, "name");
                if (string.IsNullOrWhiteSpace(templateName)) { problems.Add($"{name}: template.json has no name"); continue; }
                templates.Add(new SpfxTemplateInfo
                {
                    Name = templateName,
                    Category = Str(r, "category"),
                    Description = Str(r, "description"),
                    Version = Str(r, "version"),
                    SpfxVersion = Str(r, "spfxVersion"),
                    MinimumEngineVersion = Str(r, "minimumEngineVersion"),
                    Directory = dir,
                    UnknownFields = r.EnumerateObject().Select(p => p.Name).Where(n => !KnownFields.Contains(n)).ToList(),
                });
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                problems.Add($"{name}: {ex.Message}");
            }
        }

        return new SpfxTemplateCatalog
        {
            SourceDirectory = root,
            Templates = templates.OrderBy(t => t.Name, StringComparer.Ordinal).ToList(),
            Problems = problems,
        };
    }

    /// <summary>The template named <paramref name="name"/>, or null.</summary>
    public SpfxTemplateInfo? Find(string name) => Templates.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal));

    /// <summary>
    /// Templates the CLI at <paramref name="cliVersion"/> refuses to render because they declare a higher
    /// <c>minimumEngineVersion</c>. (The template engine ships in lockstep with the CLI, so the CLI's version is
    /// the engine version.) A template with an unparseable minimum is included too: the CLI refuses those as well.
    /// </summary>
    public IReadOnlyList<SpfxTemplateInfo> RequiringNewerEngine(string cliVersion)
    {
        var engine = SemVer.TryParse(cliVersion) ?? throw new ArgumentException($"'{cliVersion}' is not a semantic version.", nameof(cliVersion));
        return Templates.Where(t => t.MinimumEngineVersion is { } min && (SemVer.TryParse(min) is not { } m || engine < m)).ToList();
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
