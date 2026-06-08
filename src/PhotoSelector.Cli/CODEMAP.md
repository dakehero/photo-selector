# PhotoSelector.Cli Code Map

## Purpose

`PhotoSelector.Cli` is the command surface for humans, scripts, CI, and future agent/MCP integration. It should orchestrate shared services from `Core`, `Ai`, and `Config` without becoming the main home for business logic.

## Important Files

- `Program.cs`: command parsing, command handlers, JSON output models, and orchestration.

## Current Commands

- Config commands configure provider profiles, endpoint, model, API key references, API key environment variables, and output language.
- `import` indexes a directory into the shared default catalog.
- `scan <directory>` is the preferred synchronous fast path: index a directory, then rate that imported project before returning.
- `status`, `process`, `flush`, and `reset ratings` expose queued work without exposing worker internals.
- `results [directory]` summarizes rating coverage, keep/maybe/reject counts, and top candidates.
- `export <keep|maybe|reject> <directory> <target>` copies matching JPG+RAW pairs from the shared default catalog.
- `projects`, `open`, and `photos` read project context from the shared default catalog.
- Rating work is invoked through `scan` or queued `process`, not a user-facing `rate <db>` command.
- Audit product commands are not wired yet; keep their remaining work in the root TODO list instead of shipping temporary database-path commands.

## Dependencies

The CLI depends on:

- `PhotoSelector.Core` for scanning, storage, project records, and export.
- `PhotoSelector.Agent` for import workflows and queued rating workers.
- `PhotoSelector.Ai` for provider clients and rating parsing.
- `PhotoSelector.Config` for shared config and credential resolution.

## Boundaries

- Keep command handlers thin: parse arguments, call shared services, format output.
- Prefer source directories or current catalog context in user-facing commands; project IDs are acceptable for JSON/debug surfaces, and database paths should stay internal.
- Avoid provider-specific branches here when they can live in `PhotoSelector.Ai`.
- Avoid platform-specific secret code here; use `PhotoSelector.Config`.
- If `Program.cs` keeps growing, split command handlers or application services rather than adding more deep logic to one file.
