# Photo Selector Design

## Goal

Build a cross-platform, native-feeling desktop app for quickly selecting photos from a shoot. The app should help a user scan a folder, manage JPG+RAW file pairs, use AI to score and classify images, manually confirm the final choices, and export selected JPG+RAW pairs without modifying the original folder.

## Product Scope

The first version is a desktop GUI-first application with a small CLI for batch and AI-agent workflows. It is not a web app and should avoid web-shell frameworks such as Electron, Tauri, or Wails for the main UI.

The minimum useful workflow is:

1. Open a photo directory.
2. Scan supported image files.
3. Pair JPG files with matching RAW files.
4. Show JPG previews in a fast selection interface.
5. Run AI scoring and classification.
6. Let the user confirm keep, maybe, reject, and star ratings.
7. Export selected JPG+RAW pairs to a target directory by copying files.

The app must not move, delete, rename, or overwrite files in the original source directory.

## Technology Choice

Use an Avalonia and .NET solution.

Projects:

- `PhotoSelector.Core`: scanning, file type detection, JPG+RAW pairing, database models, export logic, and shared domain services.
- `PhotoSelector.Ai`: OpenAI-compatible API client, scoring prompt handling, structured AI result parsing, and an external-agent command interface reserved behind a small adapter.
- `PhotoSelector.App`: Avalonia desktop GUI.
- `PhotoSelector.Cli`: command-line interface that reuses the core and AI layers.
- `PhotoSelector.Tests`: focused tests for core behavior and CLI-critical flows.

Use SQLite as the local project database through `Microsoft.Data.Sqlite` and a small repository layer. Avoid a full ORM in the first version. Schema changes are handled by a `schema_version` table and idempotent migration scripts in the core project.

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

SQLite stores the app state for a project.

Tables:

- `projects`: project id, source directory, created time, last opened time.
- `photos`: photo id, project id, base name, jpg path, raw path, capture time, import status.
- `ratings`: photo id, provider, model, score, category, reason, raw response JSON, created time.
- `user_marks`: photo id, decision, stars, note, updated time.
- `exports`: export id, project id, target directory, filter, exported count, created time.
- `export_items`: export id, photo id, exported jpg path, exported raw path.

AI categories are `keep`, `maybe`, and `reject`.

Scores are integers from 1 to 5.

User decisions are `unreviewed`, `keep`, `maybe`, and `reject`.

## Desktop UI

The first screen is the working interface, not a landing page.

Layout:

- Left sidebar: current project directory, scan summary, filters, AI status.
- Main area: thumbnail grid using JPG previews where available.
- Right panel: selected photo details, JPG/RAW pair status, AI score, AI reason, user decision, stars, and notes.
- Top toolbar: open directory, scan, start AI scoring, export selected, settings.

Expected shortcuts:

- `1` to `5`: assign star rating.
- `K`: mark keep.
- `M`: mark maybe.
- `R`: mark reject.
- Arrow keys: move selection.
- `Space`: toggle enlarged preview.

The UI should prioritize repeated photo selection work: stable grid layout, clear selected state, visible score and decision badges, and quick keyboard operation.

## AI Scoring

The first version supports an OpenAI-compatible API provider.

Config:

- `base_url`
- `api_key`
- `model`
- scoring prompt
- maximum concurrent requests
- request timeout

The app sends a resized JPG preview to the model. RAW files are not uploaded for scoring. The model must return structured JSON:

```json
{
  "score": 4,
  "category": "keep",
  "reason": "sharp subject, good expression, clean composition"
}
```

The parser validates score range and category values. Invalid or partial results are stored as failed ratings with enough detail for retry and debugging.

An external agent command adapter is reserved for a later step. Its interface should accept a photo path and JSON context and return the same structured JSON shape on stdout.

AI provider settings are stored globally in the user's app settings. Each rating row stores the provider, model, prompt version, and raw response snapshot used for that rating so project history remains understandable after settings change.

## CLI

The CLI exists for batch use and for AI agents to call.

Commands:

```bash
photo-selector scan <directory>
photo-selector rate <project-db> --provider openai-compatible
photo-selector export <project-db> --category keep --out <directory>
photo-selector list <project-db> --json
```

`scan` creates or updates a SQLite database for the directory.

`rate` runs AI scoring for unrated or failed photos.

`export` copies selected JPG+RAW pairs to the target directory.

`list --json` emits project and photo state as structured JSON.

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

- A failed request marks only that photo as failed.
- Failed photos can be retried.
- Raw response or error details are stored in the database.

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

- `scan` creates a project database.
- `list --json` emits parseable JSON.
- `export` copies expected files from a sample project.

GUI tests:

- View-model tests for filtering, selection, rating display, and user decisions.
- Manual verification for the first Avalonia shell and image preview behavior.

## Out Of Scope For First Version

- Editing photos.
- Deleting or moving source files.
- Writing ratings into EXIF/XMP metadata.
- Full MCP server implementation.
- Cloud sync.
- Multi-user projects.
- RAW rendering beyond showing the paired file path and using JPG previews.

## Implementation Defaults

Use the standard Avalonia MVVM application template for `PhotoSelector.App`, a console template for `PhotoSelector.Cli`, and .NET class libraries for shared projects. Packaging can begin with framework-dependent builds during development and move to self-contained releases after the core workflow is working.
