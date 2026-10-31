# Tamp.SpfxCli

> Tamp wrapper for the **SharePoint Framework CLI** ([`@microsoft/spfx-cli`](https://www.npmjs.com/package/@microsoft/spfx-cli), command `spfx`), the template-based scaffolder that replaces the Yeoman generator. It provides typed `spfx create` and `list-templates` commands built for machine-controlled pipelines:
>
> - template sources pinned to a commit;
> - the CLI's environment-variable behaviours locked down;
> - a reader for the CLI's own provenance log;
> - a policy that checks the scaffold is the one you asked for.

| Package | Status |
|---|---|
| `Tamp.SpfxCli` | 0.1.0 (initial) |

```bash
dotnet add package Tamp.SpfxCli
```

Multi-targets net8 / net9 / net10. Verified against `@microsoft/spfx-cli` **0.1.0-pre.3**, the current pre-release. The CLI is pre-1.0 and changing quickly, so this package tracks it.

This package only *creates* solutions. Building, testing, packaging and gating the `.sppkg` is [`Tamp.Spfx`](https://github.com/tamp-build/tamp-spfx)'s job; the two are deliberately separate.

## A pinned, verified scaffold

```csharp
using Tamp;
using Tamp.SpfxCli;

AbsolutePath Tool => RootDirectory / "tools" / "spfx-cli";   // package.json + lockfile pinning @microsoft/spfx-cli
AbsolutePath Web => RootDirectory / "web";
const string TemplateCommit = "5753ddac6c261c5023d9218cd9bbac7640d403b7";  // a reviewed SharePoint/spfx commit
DateTimeOffset started;

Target Scaffold => _ => _.Executes(() =>
{
    started = DateTimeOffset.UtcNow;
    return SpfxCli.Create(s => s
        .SetToolDirectory(Tool)
        .SetRequirePinnedTemplates()          // refuse any template source that follows a branch
        .SetTemplateCommit(TemplateCommit)
        .SetTemplate("webpart-react")
        .SetLibraryName("contoso-intranet")
        .SetComponentName("News Rollup")
        .SetTargetDirectory(Web));            // --package-manager none: run `npm ci` yourself
});

Target VerifyScaffold => _ => _.DependsOn(nameof(Scaffold)).Executes(() =>
    new SpfxScaffoldPolicy
    {
        StartedAfter = started,               // the CLI records only successful runs
        ExpectedCliVersion = "0.1.0-pre.3",
        ExpectedTemplate = "webpart-react",
        ExpectedSpfxVersion = "1.22",
    }.Evaluate(Web).ThrowIfFailed());
```

Pointing `TargetDirectory` at a solution that already exists *adds* a component to it. The CLI merges the shared files (`package.json`, `config/config.json`, `config/package-solution.json`, `config/serve.json`) and keeps the solution's id.

## What the wrapper locks down

The CLI changes behaviour based on three environment variables, and a build machine can pass any of them to a scaffold without anyone noticing. The wrapper sets each one explicitly on the child process, so an inherited value never reaches the CLI. All three were reproduced against 0.1.0-pre.3:

| Variable | What the bare CLI does when it's inherited | Wrapper |
|---|---|---|
| `SPFX_CI_MODE=1` | An undocumented CI switch. The solution gets the fixed id `22222222-2222-2222-2222-222222222222`, and component ids are derived from the alias. Every solution scaffolded that way shares one id, so they overwrite each other in an app catalog. | Forced to `0`. Set `DeterministicIds` only for template golden-file tests. |
| `SPFX_TEMPLATE_REPO_URL` | Silently replaces the default template repository. | Cleared. Only `TemplateUrl` changes the source. |
| `GITHUB_TOKEN` | Sent to **every** template source host, including any `--remote-source`. A bad token breaks the public download (404). | Cleared unless `SetGitHubToken(secret)` is given. The token goes through the environment, never `argv`, and is redacted. It is refused when a source is on a host outside `GitHubTokenHosts` (default `github.com`). |

`SpfxCli.Raw(...)` is the escape hatch, and it leaves the environment exactly as inherited.

## Template sources

The CLI downloads templates from a **moving branch** of [SharePoint/spfx](https://github.com/SharePoint/spfx) (`version/latest`, or `version/<x>` with `--spfx-version`), as a zip, every time it runs. That repository already moves faster than the CLI: at the commit above, its templates carry a `contextSchema` field 0.1.0-pre.3 doesn't recognise. For reproducible scaffolds:

| Option | Settings | Pinned |
|---|---|---|
| A commit of the GitHub source | `SetTemplateCommit(sha)` emits `--template-url <repo>/tree/<sha>`, which the CLI downloads as that exact tree | yes |
| A vendored, reviewed copy | `AddLocalSource("<checkout>/templates")`. Offline, and the GitHub source isn't contacted by `create` | yes |
| A branch | `SetSpfxVersion("1.22")` or `SetSpfxVersion("next")` | no |
| An extra repository | `AddRemoteSource("https://github.com/<org>/<repo>/tree/<sha>")` | only with `/tree/<sha>` |

`SetRequirePinnedTemplates()` turns the right-hand column into a hard check. The wrapper also refuses combinations the CLI would silently resolve against you:
- a commit together with `--spfx-version`, because the branch wins;
- `create` with local sources together with GitHub settings, because the GitHub settings are ignored.

One quirk of the GitHub source: it treats every `template.json` anywhere in the repository as a template, so SharePoint/spfx's own test fixture (`tests/spfx-template-test/test-template`) is offered as a template named `test`. A local source reads only the immediate folders you point it at, so vendoring `templates/` avoids that.

### Vendored templates: `SpfxTemplateCatalog`

`spfx list-templates` prints a table for people. To check a vendored source before scaffolding from it, use the catalog:

```csharp
var catalog = SpfxTemplateCatalog.Load(RootDirectory / "vendor" / "spfx" / "templates");
var template = catalog.Find("webpart-react") ?? throw new InvalidOperationException("template missing");
if (catalog.RequiringNewerEngine(cliVersion: "0.1.0-pre.3").Contains(template))
    throw new InvalidOperationException($"{template.Name} needs a newer spfx-cli");
```

The catalog reads folders exactly as `--local-source` does: each immediate, non-dot subfolder that has a `template.json`. For each template it reports the name, category, template version, SPFx version and `minimumEngineVersion`, plus any `template.json` fields it doesn't model. `SemVer` orders pre-releases correctly, so `0.1.0-pre.3` < `0.1.0-pre.10` < `0.1.0`.

## The scaffold log: `.spfx-scaffold.jsonl`

`spfx create` keeps a JSONL provenance log in the solution root. Each run records:

| Event | Records |
|---|---|
| `session-started` | the CLI version |
| `template-rendered` | the template name and version, the SPFx version, and the full render context (library name, component alias, and the generated component, feature and solution ids) |
| `file-write` | one per file, with its outcome |
| `package-manager-*` | whether a package manager ran, and how it ended |

`SpfxScaffoldLog.TryRead(dir)` / `Parse(text)` reads it into typed events grouped into sessions. Events this version doesn't model are kept as `SpfxUnrecognizedEvent`, and malformed lines are reported in `ReadProblems` rather than thrown.

Three behaviours of the log, verified live, matter on a build machine:

- **Only successful runs are recorded.** The CLI saves the log at the very end, so a run that fails (an unknown template, a failed install) leaves no trace, even if it already wrote files. `SpfxScaffoldPolicy.StartedAfter` catches this: if the newest session is older than your run, your run failed.
- **Adding a component re-renders with a fresh solution id that is thrown away.** The merge into `package-solution.json` keeps the original id, but the later session's context records a new one. `SpfxScaffoldLog.OriginalSolutionId` gives the first session's id, and `config/package-solution.json` is the authority.
- **`preserved` files.** When a file already exists with different content and has no merge helper (`README.md`, `.vscode/*`, `config/typescript.json`, …), the CLI keeps the existing file and drops the template's version without saying so. Each one is reported.

## The scaffold policy

`new SpfxScaffoldPolicy { … }.Evaluate(solutionDirectory)` checks the most recent session:

| Rule | Checks | Default |
|---|---|---|
| SPFXCLI001 | No log, or no session in it | error |
| SPFXCLI002 | Unparseable log lines | error |
| SPFXCLI003 | Event kinds this reader doesn't model | note |
| SPFXCLI004 | Newest session started before `StartedAfter`: the run failed | error |
| SPFXCLI005 | Newest session rendered no template | error |
| SPFXCLI010–014 | CLI version, template, template version, SPFx version (exact or `1.22`-style prefix), component alias ≠ expected | error |
| SPFXCLI020 | The solution has the deterministic fake id | error (`AllowDeterministicSolutionId`) |
| SPFXCLI030 | A file kept its existing content instead of the template's | warning (`PreservedFileSeverity`) |
| SPFXCLI031 | Package-manager install failed | error |

`ThrowIfFailed()` fails the target and lists the errors. `Findings` carries a stable `Subject` (`file:README.md`, `template:version`, …) for evidence.

## Verbs

| Facade | Command line |
|---|---|
| `SpfxCli.Create` | `spfx create --template … --library-name … --component-name … [--component-alias] [--component-description] [--solution-name] [--target-dir] --package-manager none` |
| `SpfxCli.ListTemplates` | `spfx list-templates` (human-readable output) |
| `SpfxCli.Raw(toolDir, args…)` | `spfx <args…>`, with no validation and the inherited environment |

Every settings type also has `SetWorkingDirectory`, `SetToolDirectory`, `SetEnvironmentVariable`, `SetBinary` and `AddArgument`, plus the template-source setters above. There are object-init overloads for each verb.

**`--package-manager` is always emitted, `none` by default.** The CLI's install step runs `npm install`, not `npm ci`, so it resolves against the registry rather than a reviewed lockfile. On a build machine, run `Npm.Ci` (from [`Tamp.Npm.V10`](https://github.com/tamp-build/tamp-npm)) as its own step.

## Binary resolution

`SpfxCliResolver` looks, in order, for:

1. **`@microsoft/spfx-cli` in `node_modules`** at `ToolDirectory` or any parent, run as `node <package>/bin/spfx`. This is the recommended setup: a tool directory with an exact version in its `package.json` and a lockfile.
2. **A global install** (`npm install -g`), found next to the `spfx` command on `PATH` (Windows `%APPDATA%\npm\node_modules`, or the `<prefix>/lib/node_modules` symlink target on Linux and macOS), also run through `node`, so the version is known.
3. **The `spfx` shim**, used when the package can't be located or `node` isn't on `PATH`.

`ToolDirectory` is separate from `WorkingDirectory` because `create` scaffolds into a folder that has no `node_modules` yet. There is no `npx` fallback: a pre-release CLI changes between builds, and a build machine should run the version it was given.

The CLI requires Node `>=22.14 <23` or `>=24.12 <25`.

## Releasing

Releases follow the Tamp dogfood pattern: bump `<Version>` in `Directory.Build.props`, tag `v<X.Y.Z>`, and the release workflow runs `dotnet tamp Ci` then `dotnet tamp Push`.

## License

MIT.
