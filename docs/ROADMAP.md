# Photo Selector Roadmap

Photo Selector is moving from an AI photo-scoring CLI toward a local-first photography editor agent. The roadmap below keeps the current CLI work useful while shifting the product center toward shoot review, visual comparison, agent-assisted workflows, and long-term learning from user feedback.

## North Star

Help a photographer review a whole shoot, compare similar frames, choose stronger photos, understand why, and turn each session into feedback for the next one.

The product should not compete on "call a VLM for every image." Its value should come from local context, sequence-aware review, visible comparison, durable user feedback, and eval-driven prompt/model improvement.

## Phase 0: CLI Foundation

Status: mostly done.

Purpose: keep a reliable local engine that future GUI, agent chat, and eval harnesses can reuse.

Core capabilities:

- Catalog-first directory scanning.
- JPG/JPEG + RAW pair detection.
- Local SQLite catalog under user config.
- OpenAI-compatible provider configuration.
- System credential and environment-variable API key support.
- Structured AI rating JSON.
- Redacted audit logs with raw model responses.
- Manual marks, stars, and notes.
- Non-destructive export.
- JSON output for automation.
- NativeAOT release packages.

Quality bar:

- Existing CLI commands remain usable.
- CLI output stays stable enough for scripts and external eval tools.
- No user-facing commands expose SQLite database paths.
- No source photo is moved, deleted, renamed, or overwritten.

## Phase 1: Local Similarity Grouping

Purpose: stop brute-forcing every photo through a large VLM. Build the local grouping layer that makes shoot review practical.

Features:

- Extract capture-time and filename sequence windows.
- Read useful EXIF fields when available.
- Compute perceptual hashes such as pHash/dHash/aHash.
- Add simple color histogram features.
- Form local sequence groups from time, filename, hash, and optional similarity thresholds.
- Store group metadata and group-local ordering in the catalog.
- Expose group data through CLI JSON for later UI and agent use.

Later optional features:

- Lightweight image embeddings with CLIP, MobileCLIP, SigLIP, or DINOv2-small.
- Group-level representative frame selection.
- Duplicate and near-duplicate detection.

Success criteria:

- A 500-1000 photo shoot can be grouped locally without cloud calls.
- Obvious bursts and near-duplicates are grouped correctly.
- Different scenes that merely look similar are not globally merged without time/context support.

## Phase 2: Shoot Review Contract

Purpose: define the product object above single-photo rating.

Features:

- `shoot review` data model with summary, strengths, weaknesses, winners, rejects, and next-shoot notes.
- `group review` result with winner, alternates, reject reasons, and explanation.
- Prompt contracts for group comparison and shoot review.
- Audit records for group and shoot review calls.
- CLI JSON surfaces for:
  - project context
  - groups
  - group review
  - shoot review draft
  - learning notes

Success criteria:

- The system can review a directory as one session.
- It can explain why one frame beats nearby alternatives.
- Review output references concrete photos/groups instead of generic advice.

## Phase 3: Visual Workbench Prototype

Purpose: make the product value visible. CLI cannot be the primary experience for contact sheets and frame comparison.

Core views:

- Shoot overview.
- Contact sheet thumbnail grid.
- Sequence group strip.
- Compare view for 2-4 frames.
- Winner explanation panel.
- Learning notes panel.
- Manual feedback controls for keep/maybe/reject, stars, and notes.

Implementation direction:

- Use the shared core/catalog/provider layers.
- Keep business logic outside UI code.
- Web technology inside a local desktop shell is acceptable for fast iteration.
- Native-feeling packaging can be revisited after the workflow proves itself.

Success criteria:

- A user can open a shoot, inspect groups, compare candidates, accept/correct winners, and see a shoot-level review.
- The UI makes it easier to decide than reading CLI output.

## Phase 4: Agent Chat Workbench

Purpose: let natural language drive the visual workflow without replacing the visual evidence.

Agent chat responsibilities:

- Understand requests such as "review this shoot", "show the best landscapes", "compare these four", or "why did you pick this one".
- Call explicit internal tools rather than directly mutating storage or UI state.
- Navigate the workbench to a shoot overview, contact sheet, group, compare view, winner explanation, or learning note.
- Explain decisions using concrete photos, groups, scores, marks, and audit records.
- Ask for confirmation before export or other high-impact actions.

Initial internal tools:

- `open_shoot(directory)`
- `build_contact_sheet(project_id, filters)`
- `group_sequences(project_id, options)`
- `compare_group(group_id, photo_ids)`
- `review_group(group_id)`
- `review_shoot(project_id)`
- `mark_photo(photo_id, decision, stars, note)`
- `export_selection(project_id, category_or_selection, target)`
- `create_learning_note(scope, evidence, note)`

Success criteria:

- Chat can orchestrate real product actions.
- Visual state and chat state stay synchronized.
- User feedback from chat becomes durable catalog data.

## Phase 5: Evaluation And Photography Rubrics

Purpose: make prompt/model improvement measurable instead of vibe-based.

Features:

- External eval harness support through stable CLI JSON.
- Small personal golden set from user-marked shoots.
- Group-level pairwise labels.
- Prompt/model arena reports.
- Rubric extraction from legitimate photography sources without copying unlicensed full text or images.
- Metrics for ranking quality, category agreement, parse success, hallucination, cost, latency, and critique usefulness.

Success criteria:

- Prompt changes can be compared against fixed photo sets.
- The system improves at choosing winners within groups.
- The product can distinguish a better photography editor from a prettier text generator.

## Phase 6: Learning Loop

Purpose: turn repeated use into product memory.

Signals:

- Manual keep/maybe/reject marks.
- Star ratings.
- Notes.
- Exports.
- Future edit/publish events.
- Corrections made in compare view or agent chat.

Features:

- Recurring weakness detection.
- User taste profile.
- Shoot-to-shoot progress summaries.
- Personalized next-shoot advice.
- Learning notes tied to evidence.

Success criteria:

- The app can say what the photographer tends to do well or poorly.
- It can explain how recent decisions changed future recommendations.
- It becomes more useful after repeated shoots.

## Deferred Work

These are valuable but should not lead the roadmap right now:

- Built-in ONNX/VLM runtime and model management.
- Fully autonomous background daemon.
- Editing RAW/JPEG files.
- Writing EXIF/XMP metadata.
- Cloud sync.
- Multi-user collaboration.
- Full MCP server.

Local models remain useful as provider/backend experiments, especially for embeddings, captions, summaries, or offline fallback. They should not displace the shoot-review, grouping, visual workbench, and eval loop.

