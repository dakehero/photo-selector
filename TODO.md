# TODO

## Next CLI/Product Work

- Add catalog-first `audit` views by directory/photo identity without exposing SQLite paths.
- Improve `status [directory]` output with clearer totals: indexed, pending, rated, failed, skipped, latest run time.
- Move remaining command orchestration out of `PhotoSelector.Cli/Program.cs` into `PhotoSelector.Agent` workflows.
- Add worker lease/running state before GUI starts automatic background processing.
- Add retry policy for failed rating jobs.
- Add a real installed CLI command path so users do not need `dotnet run --project ...`.

## Explicit Non-Goals

- Do not add compatibility commands for old database-path workflows before a public release.
- Do not expose SQLite paths as normal user-facing command arguments.
- Do not use `worker` as a primary user-facing command; expose `scan`, `import`, `status`, `process`, `flush`, and result-oriented commands instead.

## Completed

- Added `results [directory]` to summarize keep/maybe/reject, rating coverage, and top candidates.
- Added catalog-first `export <keep|maybe|reject> <directory> <target>` without exposing SQLite paths.
