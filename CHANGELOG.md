# Changelog

All notable changes to `Tamp.SpfxCli` are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.2.0] - 2026-11-20 - .NET 11 in, .NET 8 and .NET 9 out

### Changed

- **Multi-targets `net10.0;net11.0`** instead of `net8.0;net9.0;net10.0` ([ADR 0015](https://github.com/tamp-build/tamp/blob/main/docs/adr/0015-target-framework-strategy.md)). .NET 11 shipped 2026-11-10 and .NET 8 and .NET 9 reached end of support the same day; the rule is to add a TFM the day a release ships and drop one the day Microsoft ends its support. `global.json` pins SDK 11.0.100.

  **If you target `net8.0` or `net9.0`, stay on the previous version** — this one carries no assets your project can resolve.

- **`Tamp.*` pinned to 1.21.0**, which carries a SARIF fix worth knowing about if this project's output is read as evidence: before 1.21.0, a SARIF result whose scanner omitted `level` was read as `none` — the least severe value — instead of the declared `warning`, on every runtime before net11 ([tamp#166](https://github.com/tamp-build/tamp/issues/166)).

- **Publishing moved to nuget.org trusted publishing (OIDC).** The `tamp_nuget_api_key` org secret expired 2026-11-01 along with every other pre-2026-08-17 nuget.org key. The publish job now mints a short-lived key from its own OIDC token, so there is no long-lived credential to rotate or leak.

## [0.1.0] — 2026-10-31

### Added

- Initial release, verified against `@microsoft/spfx-cli` 0.1.0-pre.3.
- `SpfxCli.Create` / `ListTemplates` / `Raw`, with fluent and object-init forms. `--package-manager` is always emitted (`none` by default).
- Template sources: `TemplateCommit` (pins `--template-url <repo>/tree/<sha>`), `SpfxVersion`, `TemplateUrl`, `LocalSources`, `RemoteSources`. `RequirePinnedTemplates` refuses any source that follows a branch. Combinations the CLI would resolve silently (a commit together with `--spfx-version`, or local sources together with GitHub settings on `create`) are refused.
- The CLI's environment-variable behaviours are locked down: `SPFX_CI_MODE` is forced to `0` (otherwise a fixed fake solution id), `SPFX_TEMPLATE_REPO_URL` is cleared, and an inherited `GITHUB_TOKEN` is cleared. `GitHubToken` passes a `Secret` through the environment, never on the command line, and only to `GitHubTokenHosts`.
- `SpfxCliResolver`: a local `node_modules` package at the tool directory or any parent, then a global install's package, both run through `node`, then the `spfx` shim. No `npx` fallback.
- `SpfxScaffoldLog`: a typed reader for `.spfx-scaffold.jsonl`, with sessions, preserved files and the original solution id.
- `SpfxScaffoldPolicy` (SPFXCLI001–031): stale or failed session, CLI, template and SPFx version expectations, the deterministic fake id, preserved files and install failure.
- `SpfxTemplateCatalog`: reads a vendored local template source the way `--local-source` does, with an engine-version check. `SemVer` provides pre-release-aware ordering.
