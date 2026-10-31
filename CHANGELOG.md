# Changelog

All notable changes to `Tamp.SpfxCli` are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
