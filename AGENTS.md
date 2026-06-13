# AGENTS.md

## Project Direction

Photo Selector is evolving into a photography coaching agent app. The CLI, GUI, and future agent surfaces must share the same config, auth, provider clients, database, and audit logs.

The C# codebase is the local engine. The UI shell is replaceable: Avalonia may remain useful, but future Tauri, WinUI, SwiftUI, MAUI, CLI, or MCP surfaces must reuse the same core projects instead of duplicating business logic.

## Code Map

- `src/PhotoSelector.Core`: domain model and local photo workflow.
  - `Files`: file classification such as JPG/RAW extension handling.
  - `Scanning`: directory scanning and JPG+RAW pair detection.
  - `Projects`: data records used by storage, CLI, GUI, and future agent tools.
  - `Storage`: SQLite persistence and migrations.
  - `Exporting`: file export/copy behavior.
- `src/PhotoSelector.Ai`: AI rating and photography critique provider layer.
  - `Ratings`: prompt contract, rating parser, provider clients, request payload construction, and provider factory.
- `src/PhotoSelector.Config`: shared config and credentials.
  - `Secrets`: system and memory credential providers; platform-specific providers live in separate files.
- `src/PhotoSelector.Agent`: shared workflow and worker orchestration above core/config/AI.
  - `Workflows`: internal directory indexing and future app/background workflow composition.
  - `Workers`: queued rating job processing and future worker loops.
- `src/PhotoSelector.Cli`: command surface for humans, scripts, and future agent/MCP calls.
- `src/PhotoSelector.App`: current Avalonia shell. Treat as replaceable presentation, not the source of product logic.
- `tests/PhotoSelector.Tests`: regression tests across core, config, AI, CLI, and app view models.

## Code Structure Rules

- Keep business logic out of UI projects. Put scanning, rating, storage, export, and provider behavior in `Core`, `Ai`, or `Config`.
- Keep CLI command handlers thin. If command logic grows beyond argument parsing and orchestration, extract it into core/application services.
- Put shared indexing/rating/status orchestration in `PhotoSelector.Agent`, not in CLI or UI code.
- Prefer the catalog-first workflow: import directories into the shared catalog first, then open, list, rate, and export by project identity.
- Do not add provider-specific branches deep inside CLI code. Add or modify provider adapters and factories.
- Do not let `ProjectDatabase` become a broad application service. It owns SQLite persistence only; workflow logic belongs outside it.
- Do not add new platform-specific credential code to `SecretStoreFactory`; create a dedicated provider file.
- Do not store raw image data, API keys, or unredacted requests in persistent logs.
- `PhotoSelector.App` may use mock/demo data while the UI is provisional, but production workflows must come from shared services.

## AI Provider Rules

- Prefer `IPhotoRatingClient` and `ProviderRatingClientFactory` over one-off API calls.
- Supported provider names are `openai`, `openrouter`, `openai-compatible`, `lmstudio`, and `ollama`.
- `openai` should use the official OpenAI .NET SDK where practical.
- `openrouter` and generic compatible providers may use the HTTP-compatible client when raw response capture or provider metadata matters.
- Provider implementations must preserve `AiRatingAudit` with enough data to replay or inspect a decision.

## Audit And Secret Rules

- Never store API keys in config files, databases, logs, tests, or audit records.
- Never store base64 image data URLs in audit logs.
- Redacted request logs should include provider/model, prompt, image path, and preview settings.
- Save raw model message content and raw API response when available.
- Save failed or unparsable responses too; they are useful for agent evaluation and prompt repair.

## Credentials Provider Rules

- Treat Windows, macOS, Linux, unsupported, and in-memory credential stores as first-class providers.
- Keep each platform provider in its own file under `PhotoSelector.Config/Secrets`.
- Use `SecretStoreFactory` only for selection and composition; do not place platform implementation details in the factory.
- Every `ISecretStore` must expose a stable `ProviderName` for CLI diagnostics and tests.
- `MemorySecretStore` is a real provider for tests and future agent sandboxes, not an ad hoc bypass.
- Do not shell out or P/Invoke from CLI commands directly; all credential access goes through `ISecretStore`.

## Rating Contract

AI rating JSON must include `photo_type`, `score`, `category`, `criteria`, and `reason`.

- `score` and each criterion score are `1.0` to `10.0` with exactly one decimal place.
- `category` must match score: `keep` for `8.0-10.0`, `maybe` for `5.0-7.9`, `reject` for `1.0-4.9`.
- Human-readable comments follow `output_language`; JSON property names stay stable English.

## Catalog Workflow

Default workflows use the shared SQLite catalog at `ConfigPaths.GetDatabasePath()`.

- `pick <directory>` is the primary multi-photo selection command. It indexes or updates the directory, rates pending or failed photos with the selection prompt, and returns a result summary before exiting.
- `scan <directory>` is the synchronous technical fast path for AI/automation callers. It indexes or updates the directory, rates pending or failed photos with the default rating prompt, and returns a result summary before exiting.
- `status [directory]` reports global or directory-specific work/results status.
- `reset ratings <directory>` resets AI rating outputs for that directory so the next `pick` or `scan` can evaluate it again.
- `reset ratings <directory> --with-audit` may delete audit logs too. By default, preserve audit logs for traceability.
- `results [directory]` summarizes rating coverage, keep/maybe/reject counts, and top candidates without exposing database paths.
- `export <keep|maybe|reject> <directory> <target>` copies JPG+RAW pairs whose latest AI rating matches the category into a timestamped export directory.
- `projects list --json` lists indexed projects without exposing a database path.
- `open <project-id|directory> --json` returns one project context and its photos.
- `photos list --project <project-id> --json` returns photos for one project.
- Do not add user-facing commands that require SQLite database paths. Database paths are an internal/debug concern, not product UX.

Worker implementation detail:

- CLI, GUI, and future MCP surfaces should expose product actions such as `pick`, `scan`, `status`, `results`, `export`, and `reset ratings`, not worker-management commands.
- Do not expose top-level `import`, `process`, `flush`, or `worker` commands. Directory indexing and pending-job processing are internal workflow details, and future GUI app mode may run them in the background.
- GUI startup may automatically run background processing while the app is open.
- Shared job/worker orchestration should live above `Core`, in `PhotoSelector.Agent` or a future workflow project that depends on `Core`, `Ai`, and `Config`.
- `Core` may own durable job tables and storage primitives, but it should not depend on provider clients, config resolution, or worker orchestration.

## Engineering Rules

- Add tests before changing rating behavior or provider behavior.
- Keep raw audit paths covered by tests, especially secret and image redaction.
- Prefer small provider-specific adapters over branching deeply inside CLI commands.
- Run `dotnet test` before claiming completion.

## Planning Rules

- Do not create or maintain a root `TODO.md`. Track follow-up work in Superpowers specs and plans under `docs/superpowers/`.
