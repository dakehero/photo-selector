# AGENTS.md

## Project Direction

Photo Selector is evolving into a local-first photography editor and coaching agent. Its product value should come from reviewing whole shoots, comparing similar frames, explaining editorial choices, and turning repeated photo work into long-term feedback for the photographer. Single-photo AI rating is a supporting signal, not the product's final purpose.

The CLI, future GUI, and future agent surfaces must share the same config, auth, provider clients, database, and audit logs.

The C# codebase is the local engine. The first MVP is CLI/core focused. A GUI is still intended but not implemented in this repository right now; future Tauri, WinUI, SwiftUI, MAUI, Avalonia, CLI, or MCP surfaces must reuse the same core projects instead of duplicating business logic.

Near-term product direction:

- Keep the existing CLI MVP useful for culling, rating, marking, export, and audit inspection.
- Add value above one-off model calls by moving toward shoot-level review: sequence grouping, best-frame selection within groups, session summaries, repeated failure patterns, and next-shoot coaching notes.
- Treat prompt/model arena and raw audit logs as evaluation infrastructure. External eval harnesses should be able to call the CLI and compare prompts, models, rubrics, and output stability.
- Treat user marks, exports, and future edit/publish signals as durable feedback. The catalog should help the agent learn the user's taste over time.
- Keep local model support as a provider/backend concern. Do not let local inference experiments displace the core shoot-review and eval workflow.
- Implement similar-frame grouping with local heuristics, perceptual hashes, and lightweight embeddings before using expensive VLM calls. VLMs should explain and review reduced candidate groups, not brute-force every frame.
- Future agent chat should orchestrate explicit internal tools such as open shoot, build contact sheet, group sequences, compare group, review group, review shoot, mark photo, export selection, and create learning note. Do not let the chat layer directly mutate storage or UI state without going through shared services and confirmation rules.

## Code Map

- `src/PhotoSelector.Core`: domain model and local photo workflow.
  - `Files`: file classification such as JPG/RAW extension handling.
  - `Scanning`: directory scanning and JPG+RAW pair detection.
  - `Projects`: data records used by storage, CLI, future GUI, and future agent tools.
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
- Future GUI: not implemented for the current MVP. Add a new presentation project only when the UI direction is selected.
- `tests/PhotoSelector.Tests`: regression tests across core, config, AI, CLI, and shared workflow behavior.

## Code Structure Rules

- Keep business logic out of future UI projects. Put scanning, rating, storage, export, and provider behavior in `Core`, `Ai`, `Config`, or `Agent`.
- Keep CLI command handlers thin. If command logic grows beyond argument parsing and orchestration, extract it into core/application services.
- Put shared indexing/rating/status orchestration in `PhotoSelector.Agent`, not in CLI or UI code.
- Prefer the catalog-first workflow: import directories into the shared catalog first, then open, list, rate, and export by project identity.
- Do not add provider-specific branches deep inside CLI code. Add or modify provider adapters and factories.
- Do not let `ProjectDatabase` become a broad application service. It owns SQLite persistence only; workflow logic belongs outside it.
- Do not add new platform-specific credential code to `SecretStoreFactory`; create a dedicated provider file.
- Do not store raw image data, API keys, or unredacted requests in persistent logs.
- GUI work is currently待实现. When a GUI returns, it may use mock/demo data while provisional, but production workflows must come from shared services.

## Development Loop Guardrails

- Before starting a roadmap slice, state the current goal in one sentence and name what is explicitly out of scope for that slice.
- Treat broad architecture changes, production dependencies, storage schema changes, provider selection changes, model-runtime changes, and new AI framework experiments as scope expansions. Do not slip them into a roadmap slice without calling them out first.
- Work in small, reviewable slices with a concrete acceptance check. Prefer one behavior, one command, one storage contract, or one provider path at a time.
- Keep an issue ledger while developing: current problem, known risks, non-goals, and acceptance criteria. Update it when the slice changes shape.
- Use red-green-review for behavior changes: write or update a failing test first, implement the smallest passing change, then review the diff against this file before claiming completion.
- At the end of each slice, run a drift check: confirm the change still serves shoot-level review/culling, does not turn Photo Selector into a generic album manager, and does not move workflow logic into the wrong layer.
- Stop at stable review points. A stable point means tests pass, the diff is scoped, risks are named, and the next slice is separable.
- Do not continue an open-ended self-improvement loop without a harness. The harness must include a goal, scope boundaries, automated checks, and an explicit stop condition.

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

Ratings are low-level observations. Product features should prefer higher-level concepts when possible:

- `shoot review`: a directory/session-level summary with winners, weak patterns, and next-shoot suggestions.
- `sequence/group review`: comparison of adjacent or visually similar frames with a recommended keeper.
- `learning note`: a durable observation about the user's recurring photographic strengths or weaknesses.
- `eval trace`: a stable prompt/model/output record that can be used by external harnesses.

## Catalog Workflow

Default workflows use the shared SQLite catalog at `ConfigPaths.GetDatabasePath()`.

- `pick <directory>` is the primary multi-photo selection command. It indexes or updates the directory, rates pending or failed photos with the selection prompt, and returns a result summary before exiting.
- `scan <directory>` is the synchronous technical fast path for AI/automation callers. It indexes or updates the directory, rates pending or failed photos with the default rating prompt, and returns a result summary before exiting.
- `status [directory]` reports global or directory-specific work/results status.
- `reset ratings <directory>` resets AI rating outputs for that directory so the next `pick` or `scan` can evaluate it again.
- `reset ratings <directory> --with-audit` may delete audit logs too. By default, preserve audit logs for traceability.
- `results [directory]` summarizes rating coverage, keep/maybe/reject counts, and top candidates without exposing database paths.
- `groups <directory> --json` computes in-memory sequence groups for one indexed project using staged local filters. Filename sequence matching is applied first, JPEG EXIF capture-time metadata is used when present, and an AI encoder/embedding stage is reserved for future visual similarity. It is derived workflow data and must not require SQLite group tables yet.
- `export <keep|maybe|reject> <directory> <target>` copies JPG+RAW pairs whose latest AI rating matches the category into a timestamped export directory.
- `projects list --json` lists indexed projects without exposing a database path.
- `open <project-id|directory> --json` returns one project context and its photos.
- `photos list --project <project-id> --json` returns photos for one project.
- Do not add user-facing commands that require SQLite database paths. Database paths are an internal/debug concern, not product UX.

Future catalog-first product actions should be phrased around photographer intent, not implementation machinery. Prefer commands and APIs such as `review <directory>`, `compare`, `learning notes`, or eval-oriented JSON outputs over exposing worker, job, or database details.

Worker implementation detail:

- CLI, future GUI, and future MCP surfaces should expose product actions such as `pick`, `scan`, `status`, `results`, `export`, and `reset ratings`, not worker-management commands.
- Do not expose top-level `import`, `process`, `flush`, or `worker` commands. Directory indexing and pending-job processing are internal workflow details, and future GUI app mode may run them in the background.
- Future GUI startup may automatically run background processing while the app is open.
- Shared job/worker orchestration should live above `Core`, in `PhotoSelector.Agent` or a future workflow project that depends on `Core`, `Ai`, and `Config`.
- `Core` may own durable job tables and storage primitives, but it should not depend on provider clients, config resolution, or worker orchestration.

## Engineering Rules

- Add tests before changing rating behavior or provider behavior.
- Keep raw audit paths covered by tests, especially secret and image redaction.
- Prefer small provider-specific adapters over branching deeply inside CLI commands.
- Do not implement speculative local inference, ONNX, embedding, or AI encoder work until the design is explicit enough to review. Keep these experiments behind provider/backend adapters and separate from core shoot-review workflow.
- Do not use MVP as an excuse to add future architecture prematurely. Add durable abstractions only when the current slice needs them or an existing pattern already exists.
- Schema changes must include a migration path for existing SQLite databases and a regression test for that migration. New AI-generated durable data should preserve redacted request data and raw model output when available.
- NativeAOT compatibility is a product constraint for the CLI. After changes that touch serialization, reflection-sensitive code, storage, provider clients, or command startup, verify with an AOT publish in addition to `dotnet test`.
- Prefer mature library types and parsers for external standards, binary formats, model runtimes, tensors, image metadata, EXIF/TIFF/IPTC/XMP, perceptual hashes, and embeddings. Hand-write Photo Selector domain records only when they represent product concepts or stable internal contracts.
- Keep third-party library types behind adapters when exposing them would leak implementation details into Core, CLI, future GUI, or agent contracts.
- Before hand-writing a parser or data model for a known standard, check whether an existing project dependency or a well-supported cross-platform library already provides it. If a new production dependency is needed, ask first and document why the adapter boundary is worth it.
- For JSON contracts, define DTOs and use `System.Text.Json` source-generated serializers/converters for parsing. Keep hand-written code focused on validation and domain mapping; do not manually walk JSON fields with `JsonDocument` unless preserving raw JSON shape is the actual requirement.
- Run `dotnet test` before claiming completion.

## Planning Rules

- Do not create or maintain a root `TODO.md`. For large design work, create focused Superpowers specs/plans under `docs/superpowers/` only while they are actively useful; once decisions are implemented or folded into `ROADMAP.md`, `AGENTS.md`, or `CODEMAP.md`, delete stale specs/plans instead of keeping a parallel harness.
- Keep `CODEMAP.md` files as coarse navigation aids, not exhaustive inventories. Update them when adding or removing top-level areas, user-facing commands, storage domains, provider surfaces, or major test areas. If a codemap starts duplicating implementation details that change every slice, simplify it instead of expanding it.
