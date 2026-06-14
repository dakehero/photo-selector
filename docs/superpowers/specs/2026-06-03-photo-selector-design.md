# Photo Selector Design

## Goal

Build a cross-platform, native-feeling desktop app for quickly selecting photos from a shoot. The app should help a user scan a folder, manage JPG+RAW file pairs, use AI to score and classify images, manually confirm the final choices, and export selected JPG+RAW pairs without modifying the original folder.

## Product Scope

The first version is a local catalog-first CLI/core application. The GUI remains important but is待实现 and should be treated as a replaceable shell over the shared engine. It is not a web app by default and should avoid web-shell frameworks for the primary UI unless the product direction is explicitly changed.

The minimum useful workflow is:

1. Import or scan a photo directory into the shared catalog.
2. Pair JPG files with matching RAW files.
3. Queue or synchronously run AI scoring and classification.
4. Query status and results from CLI or future GUI/agent surfaces.
5. Let the user confirm keep, maybe, reject, and star ratings.
6. Export selected JPG+RAW pairs to a target directory by copying files.

The app must not move, delete, rename, or overwrite files in the original source directory.

## Technology Choice

Use a .NET local engine with a NativeAOT-friendly CLI. The desktop GUI is待实现 and the UI framework remains undecided.

Projects:

- `PhotoSelector.Core`: scanning, file type detection, JPG+RAW pairing, database models, rating job records, export logic, and storage primitives.
- `PhotoSelector.Ai`: provider clients, scoring prompt handling, structured AI result parsing, raw audit capture, and provider factories.
- `PhotoSelector.Config`: shared config paths, provider profiles, and secret-store integration.
- `PhotoSelector.Agent`: import workflows, queued rating jobs, and worker orchestration shared by CLI, future GUI, and future MCP/agent surfaces.
- Future GUI project:待实现. It must reuse Core/Ai/Config/Agent instead of owning product logic.
- `PhotoSelector.Cli`: command-line interface that reuses the Agent/Core/AI/Config layers.
- `PhotoSelector.Tests`: focused tests for core behavior and CLI-critical flows.

Use SQLite as the shared local catalog at `ConfigPaths.GetDatabasePath()`, currently `~/.photo-selector/photo-selector.db` unless `PHOTO_SELECTOR_CONFIG_HOME` overrides it. Do not put the default SQLite database inside the photo directory. Avoid a full ORM in the first version. Schema changes are handled by a `schema_version` table and idempotent migrations in the core project.

Use JSON as an interchange format for CLI output, AI-agent integration, and optional result export.

## Supported Photo Files

The first version supports:

- JPG/JPEG: `.jpg`, `.jpeg`
- RAW: `.cr3`, `.cr2`, `.nef`, `.arw`, `.raf`, `.rw2`, `.dng`, `.orf`

Pairing uses the file stem inside the same directory. For example:

- `IMG_0012.JPG`
- `IMG_0012.CR3`

become one photo item with a JPG preview path and a RAW companion path.

If a JPG or RAW file has no matching companion, the scanner still imports it as a standalone photo item. The UI should make missing pairs visible but should not treat them as errors.

## Local Data Model

SQLite stores the shared catalog state.

Tables:

- `projects`: project id, source directory, created time, last opened time.
- `photos`: photo id, project id, base name, jpg path, raw path, capture time, import status.
- `ratings`: photo id, provider, model, photo type, score, category, criteria JSON, reason, created time.
- `rating_audit_logs`: photo id, rating id, provider, model, prompt, redacted request JSON, raw model message, raw provider response, HTTP status, error, created time.
- `rating_jobs`: project id, photo id, status, attempts, last error, created time, updated time.
- `user_marks`: photo id, decision, stars, note, updated time.
- `exports`: export id, project id, target directory, filter, exported count, created time.
- `export_items`: export id, photo id, exported jpg path, exported raw path.

AI categories are `keep`, `maybe`, and `reject`.

Scores are decimal values from `1.0` to `10.0` with exactly one decimal place. Each criterion score follows the same format.

User decisions are `unreviewed`, `keep`, `maybe`, and `reject`.

## Future Desktop UI

The desktop UI is待实现 and is outside the current CLI/core MVP. When it returns, the first screen should be the working interface, not a landing page.

Layout:

- Left sidebar: current project directory, scan summary, filters, AI status.
- Main area: thumbnail grid using JPG previews where available.
- Right panel: selected photo details, JPG/RAW pair status, AI score, AI reason, user decision, stars, and notes.
- Top toolbar: open/import directory, scan synchronously, show processing status, export selected, settings.

Expected shortcuts:

- `1` to `5`: assign star rating.
- `K`: mark keep.
- `M`: mark maybe.
- `R`: mark reject.
- Arrow keys: move selection.
- `Space`: toggle enlarged preview.

The UI should prioritize repeated photo selection work: stable grid layout, clear selected state, visible score and decision badges, and quick keyboard operation.

## AI Scoring

The first version supports OpenAI-compatible providers and provider-specific adapters where useful. Supported provider names currently include `openai`, `openrouter`, `openai-compatible`, `lmstudio`, and `ollama`.

Config:

- `base_url`
- `model`
- `api_key_ref` for system secret stores
- `api_key_env` for CLI/CI environments
- output language
- scoring prompt
- maximum concurrent requests
- request timeout

The app sends a resized JPG preview to the model. RAW files are not uploaded for scoring. The model must return structured JSON:

```json
{
  "photo_type": "street",
  "score": 8.4,
  "category": "keep",
  "criteria": [
    { "name": "impact", "score": 8.5, "comment": "strong moment" }
  ],
  "reason": "sharp subject, good expression, clean composition"
}
```

The parser validates one-decimal score format, score range, criteria score range, and category values. Invalid or partial results are stored in audit logs and failed jobs with enough detail for retry and debugging.

An external agent command adapter is reserved for a later step. Its interface should accept a photo path and JSON context and return the same structured JSON shape on stdout.

AI provider settings are stored globally in the user's app settings. Secrets are not stored in config, database, or logs. Each rating and audit row stores enough non-secret provider/model/prompt/response detail so project history remains understandable after settings change.

## CLI

The CLI exists for batch use and for AI agents to call. It must not expose SQLite database paths in normal user-facing commands.

Commands:

```bash
photo-selector pick <directory>
photo-selector rate <image>
photo-selector coach <image>
photo-selector arena <directory> --models <model-a,model-b> [--limit <count>]
photo-selector arena list [directory] [--json]
photo-selector arena show <run-id> [--json]
photo-selector scan <directory>
photo-selector status [directory]
photo-selector reset ratings <directory> [--with-audit]
photo-selector results [directory]
photo-selector results [directory] --photo <photo-id|base-name> [--audit] [--json]
photo-selector mark <directory> <photo-id|base-name> --decision <decision> [--stars <0-5>] [--note <text>] [--json]
photo-selector export <keep|maybe|reject> <directory> <target>
photo-selector projects list --json
photo-selector open <project-id|directory> --json
photo-selector photos list --project <project-id> --json
```

`pick` is the product-facing multi-photo culling command. It imports or updates a directory, rates pending or failed photos with the selection prompt, supports configurable preview quality and concurrency, and returns ranked results before exiting.

`rate` scores one image with the rating prompt. `coach` critiques one image with the coaching prompt. Both are useful for testing prompts and for future agent loops.

`arena` compares multiple models against the same imported photo set and records model-by-model results for later inspection.

`scan` is the synchronous fast path. It imports or updates the directory, processes pending rating jobs for that directory, and returns a result summary before exiting.

`reset ratings` removes AI rating outputs for a directory and requeues work. It preserves audit logs by default; `--with-audit` deletes audit logs too.

`results` summarizes rating coverage, keep/maybe/reject counts, and top candidates for all projects or one directory.

`results --photo <photo-id|base-name> --audit` shows a single photo's result and redacted AI audit trail without exposing SQLite paths.

`mark` saves the user's manual decision, star rating, and optional note for one photo. It must not overwrite AI ratings; manual marks are stored separately so AI output and human review remain distinguishable.

`export` copies JPG+RAW pairs whose latest AI rating matches the requested category into a timestamped export directory under the target root.

`projects`, `open`, and `photos` expose catalog state for humans, scripts, and future agent surfaces.

## Export Behavior

Export is non-destructive.

The app copies selected files to a target directory. When a photo has both JPG and RAW paths, both are copied. When a photo has only one side of the pair, only the available file is copied.

The source directory is never changed.

Each export creates a timestamped subdirectory under the user-selected target directory, such as `photo-selector-export-20260603-153000`. This avoids overwriting destination files and makes export history easy to inspect.

## Error Handling

Scanning:

- Unsupported files are ignored.
- Unmatched JPG or RAW files are imported as standalone photo items.
- Permission errors are reported without aborting the whole scan when possible.

AI scoring:

- A failed request marks only that rating job as failed.
- Failed jobs can be retried by product commands that revisit pending or failed work, such as `pick` and `scan`.
- Raw response or error details are stored in audit logs with secrets and image data redacted.

Export:

- Missing source files are skipped and reported.
- Existing destination files are not overwritten.
- Export history records the attempted filter and the number of copied items.

## Testing

Core tests:

- File type detection.
- JPG+RAW pairing across common extensions.
- Standalone JPG and standalone RAW handling.
- SQLite create, insert, update, query flows.
- AI result JSON validation.
- Export copy behavior for paired and unpaired files.

CLI tests:

- `scan` imports and synchronously processes rating jobs.
- `pick` imports a directory, applies the selection prompt, and returns ranked results.
- `rate` and `coach` process a single image with separate prompt contracts.
- `arena` records multi-model comparisons for the same photo set.
- `status` and `reset ratings` follow the catalog-first semantics.
- `results [directory]` summarizes rating coverage, keep/maybe/reject counts, and top candidates.
- `results --photo <photo-id|base-name> --audit --json` emits a parseable decision trace.
- `mark <directory> <photo-id|base-name>` persists manual review decisions separately from AI ratings.
- `export <keep|maybe|reject> <directory> <target>` copies matching JPG+RAW pairs without requiring SQLite paths.
- `projects/open/photos --json` emit parseable JSON without requiring SQLite paths.

Future GUI tests:

- View-model tests for filtering, selection, rating display, and user decisions.
- Manual verification for the first shell and image preview behavior once a GUI framework is selected.

## Out Of Scope For First Version

- Editing photos.
- Deleting or moving source files.
- Writing ratings into EXIF/XMP metadata.
- Full MCP server implementation.
- Cloud sync.
- Multi-user projects.
- RAW rendering beyond showing the paired file path and using JPG previews.

## Implementation Defaults

Use a console template for `PhotoSelector.Cli` and .NET class libraries for shared projects. The GUI project is待实现; choose its framework later and keep it as a replaceable presentation layer. Packaging can begin with CLI self-contained or NativeAOT builds after the core workflow is working.
