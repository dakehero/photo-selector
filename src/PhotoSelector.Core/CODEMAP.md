# PhotoSelector.Core Code Map

## Purpose

`PhotoSelector.Core` contains the domain model and local photo workflow that every surface should share. GUI, CLI, and future MCP/agent tools should depend on this project for photo scanning, JPG+RAW pairing, persistence records, and export behavior.

## Main Areas

- `Files`: classifies supported photo file extensions.
- `Scanning`: scans files or directories and groups JPG+RAW pairs by base name.
- `Projects`: immutable records used by storage, CLI, GUI, and agent-facing workflows.
- `Storage`: SQLite project, photo, rating, audit, and rating-job persistence. The default database path is selected by `PhotoSelector.Config`.
- `Exporting`: copies selected photo files into timestamped export folders.

## Important Files

- `Files/PhotoFileClassifier.cs`: central extension classifier for JPEG, RAW, and unsupported files.
- `Scanning/PhotoScanner.cs`: directory and file-list scanner.
- `Scanning/PhotoPair.cs`: JPG+RAW pair record.
- `Storage/ProjectDatabase.cs`: SQLite schema, project/photo/rating persistence, and audit-log persistence.
- `Projects/PhotoItem.cs`: persisted photo row shape used by consumers.
- `Projects/PhotoRating.cs`: parsed rating result attached to a photo.
- `Projects/PhotoRatingAuditLog.cs`: raw and redacted AI decision trace.
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
