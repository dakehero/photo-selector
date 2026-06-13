# TODO

## Next CLI/Product Work

- Add catalog-first `audit` views by directory/photo identity without exposing SQLite paths.
- Improve `status [directory]` output with clearer totals: indexed, pending, rated, failed, skipped, latest run time.
- Move remaining command orchestration out of `PhotoSelector.Cli/Program.cs` into `PhotoSelector.Agent` workflows.
- Add retry policy for failed rating jobs.
- Add a real installed CLI command path so users do not need `dotnet run --project ...`.

## Explicit Non-Goals

- Do not add compatibility commands for old database-path workflows before a public release.
- Do not expose SQLite paths as normal user-facing command arguments.
- Do not expose worker-management commands such as `import`, `process`, `flush`, or `worker`. App-mode background work is an internal workflow detail.

## Completed

- Added `results [directory]` to summarize keep/maybe/reject, rating coverage, and top candidates.
- Added catalog-first `export <keep|maybe|reject> <directory> <target>` without exposing SQLite paths.
- Removed unused top-level `import`, `process`, and `flush` commands from the CLI surface.
- Added `help` and `help --json` as the shared human/agent command discovery layer.
