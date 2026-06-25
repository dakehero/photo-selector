# PhotoSelector.Core Code Map

## Purpose

`PhotoSelector.Core` contains the shared local engine. GUI, CLI, and future MCP/agent tools should depend on this project for file classification, metadata reading, grouping primitives, scanning, JPG+RAW pairing, persistence records, lifecycle state, and export behavior.

## Main Areas

- `Files`: classifies supported photo file extensions.
- `Scanning`: scans files or directories and groups JPG+RAW pairs by base name.
- `Metadata`: reads JPEG metadata such as EXIF capture time.
- `Grouping`: computes derived in-memory groups from filename sequence, capture-time metadata, and future embedding stages.
- `Projects`: immutable records used by storage, CLI, GUI, and agent-facing workflows.
- `Storage`: SQLite project, photo lifecycle, rating, audit, rating-job, arena, user-mark, and group-review persistence. The default database path is selected by `PhotoSelector.Config`.
- `Exporting`: copies selected photo files into timestamped export folders.

## Important Files

- `Files/PhotoFileClassifier.cs`: central extension classifier for JPEG, RAW, and unsupported files.
- `Scanning/PhotoScanner.cs`: directory and file-list scanner.
- `Scanning/PhotoPair.cs`: JPG+RAW pair record.
- `Metadata/PhotoMetadataReader.cs`: metadata reader used by grouping and scanning workflows.
- `Grouping/FilenameSequenceGrouper.cs`: local sequence grouping pipeline for adjacent or similarly named frames.
- `Grouping/IPhotoGroupingEncoder.cs`: adapter boundary for future embedding-based grouping.
- `Storage/ProjectDatabase.cs`: database connection wrapper; domain-specific persistence lives in sibling partial files.
- `Storage/ProjectDatabase.Schema.cs`: SQLite schema creation and migrations.
- `Storage/ProjectDatabase.Rows.cs`: linq2db table row mappings.
- `Storage/ProjectDatabase.*.cs`: partial persistence domains for projects, photos, ratings, audit logs, jobs, arena runs, user marks, and group reviews.
- `Projects/PhotoItem.cs`: persisted photo row shape used by consumers.
- `Projects/PhotoImportStatus.cs`: lifecycle states such as imported, changed, and missing.
- `Projects/PhotoRating.cs`: parsed rating result attached to a photo.
- `Projects/PhotoRatingAuditLog.cs`: raw and redacted AI decision trace.
- `Projects/PhotoUserMark.cs`: manual decision, star rating, and note attached to one photo.
- `Projects/GroupReview.cs`: persisted group review snapshot and audit data.
- `Projects/RatingJob.cs`: durable queued rating work item.
- `Projects/RatingJobSummary.cs`: pending/completed/failed job counts.
- `Exporting/ExportService.cs`: export/copy behavior.

## Dependencies

This project should not depend on UI, CLI, provider clients, or platform-specific credential stores. Higher-level projects depend on `Core`, not the other way around.

## Boundaries

- Keep provider calls out of this project; use `PhotoSelector.Ai`.
- Keep config and secret resolution out of this project; use `PhotoSelector.Config`.
- Keep command-line parsing and UI state out of this project.
- `ProjectDatabase` should stay persistence-focused. If workflow logic grows, extract application services instead of adding more orchestration here.
- Import/open/rate/export workflow orchestration belongs above this project; `Core` should provide durable records and storage primitives.
- Groups are derived workflow data unless explicitly saved as review snapshots. Do not add durable group tables just because the grouping algorithm emits groups.
