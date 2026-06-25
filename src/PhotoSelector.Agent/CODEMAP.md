# PhotoSelector.Agent Code Map

## Purpose

`PhotoSelector.Agent` owns shared workflow and worker orchestration above `Core`, `Ai`, and `Config`. CLI, GUI, and future MCP surfaces should call this project instead of duplicating import, rating, and future review loops.

## Main Areas

- `Workflows`: product workflow composition such as importing a directory, enqueueing rating work, and building local shoot review drafts.
- `Workers`: job processors that execute queued work and write results/audit logs.

## Important Files

- `Workflows/ImportWorkflow.cs`: imports a directory into the catalog and enqueues rating jobs.
- `Workflows/ShootReviewDraftBuilder.cs`: builds a local shoot review draft from catalog ratings, groups, and saved group review snapshots.
- `Workers/RatingWorker.cs`: processes pending rating jobs with an `IPhotoRatingClient`.

## Dependencies

This project depends on:

- `PhotoSelector.Core` for scanning records, storage, projects, photos, ratings, and job persistence.
- `PhotoSelector.Ai` for rating clients and rating request contracts.
- `PhotoSelector.Config` for AI profile models used by worker options.

## Boundaries

- Keep user-facing command parsing out of this project.
- Keep UI state out of this project.
- Keep provider-specific HTTP details in `PhotoSelector.Ai`.
- Keep database schema and durable job tables in `PhotoSelector.Core`.
- Keep command names, JSON formatting, and human output in presentation layers such as `PhotoSelector.Cli`.
