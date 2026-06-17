# Photo Selector Design

## Goal

Build a local-first photography editor and coaching agent. The app should help a photographer review a whole shoot, compare similar frames, explain editorial choices, identify repeated weaknesses, and turn each session into feedback for the next one.

Quick culling remains important, but one-off AI scoring is not the product's core value. The product should become useful because it accumulates local context: JPG+RAW pairs, AI observations, user marks, exports, audit trails, prompt/model comparisons, and eventually edit/publish signals.

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

The next product layer is shoot review:

1. Treat a directory as one photographic session, not just a bag of independent files.
2. Group adjacent or visually similar frames into sequences.
3. Recommend the strongest frame in each sequence and explain why it beats nearby alternatives.
4. Produce a session-level review: strongest candidates, weak patterns, missed opportunities, and next-shoot advice.
5. Preserve human feedback so later reviews can learn from the user's real decisions.

The current CLI commands are still useful because they provide the storage, audit, provider, export, and JSON surfaces needed for this larger workflow.

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

Future shoot-review tables should be added when the workflow is implemented:

- `photo_groups`: project id, group type, representative photo id, grouping reason, created time.
- `photo_group_items`: group id, photo id, order, similarity score, group-local rank.
- `shoot_reviews`: project id, provider, model, prompt version, summary, strengths, weaknesses, next-shoot advice, created time.
- `learning_notes`: project id or global scope, topic, evidence JSON, note, confidence, created time.
- `eval_runs`: prompt version, model, dataset or project selector, metrics JSON, created time.

AI categories are `keep`, `maybe`, and `reject`.

Scores are decimal values from `1.0` to `10.0` with exactly one decimal place. Each criterion score follows the same format.

User decisions are `unreviewed`, `keep`, `maybe`, and `reject`.

Design note: ratings are evidence, not the final product object. The catalog should make it possible to explain why a photo was selected, why a nearby frame was rejected, and how user feedback changed the agent's future decisions.

## Local Similarity Grouping

Similar-frame grouping should be a local, cheap, deterministic workflow. It should not depend on a large vision-language model. Large models are better reserved for editorial judgment, group-level explanation, and shoot-level review after the candidate set has been reduced.

Recommended grouping layers:

1. **File and time heuristics**: use adjacent file names, capture timestamps, same directory, and nearby EXIF values to create candidate windows. This catches burst sequences and same-scene variations without model inference.
2. **Perceptual hashes**: compute pHash/dHash/aHash and simple color histograms to detect near-duplicates, slight exposure edits, and very similar frames on CPU.
3. **Lightweight image embeddings**: optionally use a local embedding model such as CLIP, MobileCLIP, SigLIP, or DINOv2-small to compare visual similarity with cosine distance.
4. **Graph or hierarchical clustering**: combine the signals inside a time/file candidate window and form groups. Avoid global-only embedding clustering, which can incorrectly group visually similar photos from unrelated shoots.

The workflow should reduce expensive VLM calls:

```text
1000 photos -> local grouping -> 100-200 candidate frames -> VLM review/explanation
```

Group records should be auditable. Store the grouping method, thresholds, representative photo, similarity scores, and group-local order when the feature is implemented. Grouping is a core product capability because it enables contact-sheet review and pairwise editorial decisions.

## Future Desktop UI

The desktop UI is待实现 and is outside the current CLI/core MVP. When it returns, the first screen should be the working interface, not a landing page.

Layout:

- Left sidebar: current project directory, scan summary, filters, AI status.
- Main area: thumbnail grid using JPG previews where available.
- Right panel: selected photo details, JPG/RAW pair status, group membership, AI score, AI reason, user decision, stars, and notes.
- Top toolbar: open/import directory, scan synchronously, show processing status, export selected, settings.

Shoot-review UI requirements:

- Contact sheet view for the whole shoot.
- Group strip for similar or adjacent frames.
- Compare view for 2-4 frames in one group.
- Visible group winner, alternates, rejects, and explanation.
- Session review panel with strengths, weak patterns, and next-shoot notes.
- Fast feedback controls so user corrections become durable training/eval signals.

Agent chat should be a first-class UI surface alongside the visual workbench. It should not replace contact sheets or compare views; it should coordinate them.

Agent chat responsibilities:

- Accept natural-language goals such as "review this shoot", "show me the best landscape frames", "compare these four", or "why did you pick this one".
- Call built-in tools for scan/index, local grouping, contact sheet generation, group comparison, VLM review, export preview, and learning-note creation.
- Navigate or update the visual workbench: open a shoot overview, focus a sequence group, pin a compare view, or show the evidence behind a winner.
- Explain decisions with references to concrete photos, groups, scores, user marks, and audit records.
- Ask for confirmation before destructive or high-impact actions. It must never delete, move, rename, overwrite, or export files without explicit user intent.
- Record user feedback from natural language when clear, such as "mark this one as keep", "the third frame is actually better", or "remember that I prefer the darker edit".

The ideal interaction is conversational plus visual: the user can ask the agent to do work in natural language, but the final selection, comparison, and learning loop remain grounded in visible photos and explicit feedback.

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

## Prompt And Model Evaluation

Prompt/model evaluation is a product capability, but the first implementation should not bury a complex eval harness inside the CLI. Photo Selector should expose stable, machine-readable commands and audit logs so external tools can evaluate it.

Near-term eval direction:

- Keep `rate`, `pick`, `coach`, `arena`, `results --audit --json`, and raw audit logs stable enough for external harnesses.
- Use fixed photo sets, human labels, pairwise preferences, and prompt versions outside the core CLI.
- Compare prompt/model combinations for selection accuracy, ranking quality, JSON parse success, cost, latency, hallucination rate, and critique usefulness.
- Treat public photography/aesthetic datasets and the user's own marked shoots as evaluation sources.
- Use photography websites, books, and magazines mainly to extract rubrics and critique dimensions, not as unlicensed training text or image corpora.

The evaluation goal is to answer whether the system behaves like a reliable photography editor, not whether it can generate a pretty paragraph for one image.

## Agent Automation Boundary

Photo Selector should support agent automation, but the first valuable agent behavior should be bounded workflow orchestration rather than an autonomous daemon.

The future agent chat UI should call explicit internal tools rather than reaching into UI or database internals directly.

Initial internal tool set:

- `open_shoot(directory)`: scan or open a catalog project and return shoot context.
- `build_contact_sheet(project_id, filters)`: return thumbnail grid data for the visual workbench.
- `group_sequences(project_id, options)`: run local similarity grouping and return groups.
- `compare_group(group_id, photo_ids)`: prepare a 2-4 photo compare view and group-local evidence.
- `review_group(group_id)`: call the configured VLM on selected candidates and produce winner explanation.
- `review_shoot(project_id)`: synthesize shoot overview, strengths, weak patterns, and next-shoot notes from groups, ratings, marks, and audit data.
- `mark_photo(photo_id, decision, stars, note)`: save explicit user feedback.
- `export_selection(project_id, category_or_selection, target)`: stage or perform non-destructive export after user confirmation.
- `create_learning_note(scope, evidence, note)`: store durable coaching observations.

Useful near-term automation:

- Run deterministic local grouping before calling a VLM.
- Select a small set of representative frames per group for expensive review.
- Retry failed AI jobs with preserved audit logs.
- Generate a shoot-review draft from existing ratings, groups, and user marks.
- Export machine-readable JSON for external eval harnesses and future MCP tools.

Avoid near-term automation that hides product decisions:

- Do not automatically delete, move, rename, or overwrite source photos.
- Do not silently reprocess entire catalogs without a user command or future GUI session.
- Do not make worker management a user-facing product surface.
- Do not implement a broad autonomous agent before the grouping, review, and feedback contracts are stable.

The agent layer should behave like a careful photography assistant: it prepares groups, proposes winners, explains tradeoffs, and records feedback, while the user remains in control of final decisions.

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

Future CLI direction:

```bash
photo-selector review <directory> [--json]
photo-selector compare <directory> --group <group-id> [--json]
photo-selector learning notes [directory] [--json]
```

`review` should become the product-facing shoot review command. It should scan or update a directory, group similar frames, recommend winners per group, summarize the shoot, and produce next-shoot coaching notes.

`compare` should explain why one frame in a group is stronger than its neighbors.

`learning notes` should expose durable observations about recurring strengths and weaknesses. These notes should come from user-confirmed history, not from a single model response.

Do not implement these commands until the grouping and review contracts are designed and tested. They are listed here to steer product direction away from endless single-image rating features.

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
- Built-in local ONNX/VLM runtime and model management. Local model support should remain a provider/backend experiment until the shoot-review and eval workflows justify the added complexity.

## Implementation Defaults

Use a console template for `PhotoSelector.Cli` and .NET class libraries for shared projects. The GUI project is待实现; choose its framework later and keep it as a replaceable presentation layer. Packaging can begin with CLI self-contained or NativeAOT builds after the core workflow is working.
