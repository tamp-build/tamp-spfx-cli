# Test fixtures

- `two-sessions.spfx-scaffold.jsonl` and `package-solution.json` are real output of `@microsoft/spfx-cli` 0.1.0-pre.3: `spfx create --template webpart-minimal` and then `--template extension-application-customizer` into the same folder, with templates pinned to SharePoint/spfx commit `5753ddac6c261c5023d9218cd9bbac7640d403b7`.
- `templates/webpart-minimal/template.json` is copied from that commit. The other folders under `templates/` are hand-made cases: a template that needs a newer engine, a dot folder, unparseable JSON, and a folder with no `template.json`.
