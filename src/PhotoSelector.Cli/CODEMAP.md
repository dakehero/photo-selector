# PhotoSelector.Cli Code Map

## Purpose

`PhotoSelector.Cli` is the command surface for humans, scripts, CI, and future agent/MCP integration. It should orchestrate shared services from `Core`, `Ai`, and `Config` without becoming the main home for business logic.

## Important Files

- `Program.cs`: command parsing, command handlers, JSON output models, help schema, and orchestration. Keep it thin; move growing workflow logic into shared services.

## Current Commands

- Config commands configure provider profiles, endpoint, model, API key references, API key environment variables, and output language.
- `auth login/status/logout` stores shared API key references through `PhotoSelector.Config`; `auth status --verbose` reports secret-store diagnostics for setup troubleshooting.
- `help` and `help --json` expose the human overview and machine-readable command schema for agents.
- `pick <directory>` is the primary product command for multi-photo selection: index a directory, rate pending or failed photos with the default or configured prompt, then print results.
- `scan <directory>` is the synchronous automation/debug fast path: index a directory, then rate pending or failed photos with the default rating prompt before returning.
- `status` and `reset ratings` expose catalog state and rerating control without exposing worker-management commands.
- `results [directory]` summarizes rating coverage, keep/maybe/reject counts, and top candidates.
- `results [directory] --photo <photo-id|base-name> --audit [--json]` shows one photo result with redacted request and raw model audit logs for decision tracing.
- `groups <directory> --json` computes in-memory sequence groups for one indexed project.
- `review group <directory> <group-id> [--winner <photo-id|base-name> --reason <text>] [--json]` saves a group review snapshot. Without `--winner`, it uses the configured AI provider to compare the group and select a winner.
- `mark <directory> <photo-id|base-name>` saves manual keep/maybe/reject/unreviewed decisions, stars, and notes without changing AI ratings.
- `export <keep|maybe|reject> <directory> <target>` copies matching JPG+RAW pairs from the shared default catalog.
- `projects`, `open`, and `photos` read project context from the shared default catalog.
- Rating and review work is invoked through product commands such as `pick`, `scan`, `rate`, `coach`, `arena`, `groups`, and `review group`, not user-facing worker-management commands.

## Dependencies

The CLI depends on:

- `PhotoSelector.Core` for scanning, storage, project records, and export.
- `PhotoSelector.Agent` for import workflows and queued rating workers.
- `PhotoSelector.Ai` for provider clients and rating parsing.
- `PhotoSelector.Config` for shared config and credential resolution.

## Boundaries

- Keep command handlers thin: parse arguments, call shared services, format output.
- Keep human usage text and agent JSON help derived from the shared help metadata in `Program.cs`.
- Prefer source directories or current catalog context in user-facing commands; project IDs are acceptable for JSON/debug surfaces, and database paths should stay internal.
- Do not reintroduce top-level `import`, `process`, `flush`, or `worker` commands. They are internal workflow concepts, not product CLI verbs.
- Avoid provider-specific branches here when they can live in `PhotoSelector.Ai`.
- Avoid platform-specific secret code here; use `PhotoSelector.Config`.
- Do not expose SQLite database paths in normal user-facing commands. Use directories, project IDs for machine surfaces, or shared catalog context.
- If `Program.cs` keeps growing, split command handlers or application services rather than adding more deep logic to one file.
