# In-Memory Sequence Grouping Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build Phase 1 staged sequence grouping as an in-memory Core service exposed through CLI JSON.

**Architecture:** `PhotoSelector.Core.Metadata` reads JPEG EXIF `DateTimeOriginal` into existing catalog photo metadata. `PhotoSelector.Core.Grouping` computes deterministic groups from `PhotoItem` records using filename sequence candidates, capture-time filtering when metadata is present, and an explicit reserved AI encoder stage. `PhotoSelector.Cli` opens an indexed project, calls the grouping service, and emits source-generated JSON without database schema changes.

**Tech Stack:** C#/.NET 10, xUnit, System.CommandLine, System.Text.Json source generation.

---

### Task 1: Core Grouping DTOs And Service

**Files:**
- Create: `src/PhotoSelector.Core/Grouping/PhotoGroup.cs`
- Create: `src/PhotoSelector.Core/Grouping/PhotoGroupItem.cs`
- Create: `src/PhotoSelector.Core/Grouping/FilenameSequenceGrouper.cs`
- Test: `tests/PhotoSelector.Tests/FilenameSequenceGrouperTests.cs`

- [ ] Write failing xUnit tests for shared-prefix grouping, filename gap splitting, capture-time gap splitting, missing capture-time tolerance, singleton omission, and no-trailing-number omission.
- [ ] Run `dotnet test --filter FilenameSequenceGrouperTests` and verify failures are caused by missing types.
- [ ] Add immutable records for group and group item output.
- [ ] Implement filename parsing with trailing-number extraction and deterministic ordering.
- [ ] Run `dotnet test --filter FilenameSequenceGrouperTests` and verify all grouping tests pass.

### Task 2: CLI Groups Command

**Files:**
- Modify: `src/PhotoSelector.Cli/Program.cs`
- Test: `tests/PhotoSelector.Tests/CliSmokeTests.cs`

- [ ] Write a failing CLI smoke test that scans a directory, runs `groups <directory> --json`, and asserts stable JSON.
- [ ] Run the CLI smoke test filter and verify failure is caused by the missing command.
- [ ] Add `groups <directory> --json` to the root command.
- [ ] Add CLI JSON records and `CliJsonContext` metadata.
- [ ] Add help catalog entry for `groups`.
- [ ] Run the CLI smoke test filter and verify it passes.

### Task 3: Verification

**Files:**
- All changed files.

- [ ] Run `dotnet test`.
- [ ] Review `git diff --stat` and `git diff`.
- [ ] Report exact verification output and remaining gaps.

### Task 4: JPEG EXIF Capture Time

**Files:**
- Create: `src/PhotoSelector.Core/Metadata/PhotoMetadataReader.cs`
- Modify: `src/PhotoSelector.Core/Storage/ProjectDatabase.cs`
- Test: `tests/PhotoSelector.Tests/PhotoMetadataReaderTests.cs`
- Test: `tests/PhotoSelector.Tests/ProjectDatabaseTests.cs`

- [ ] Write failing tests for JPEG EXIF `DateTimeOriginal` parsing and catalog `capture_time` storage.
- [ ] Run the focused tests and verify failures are caused by missing metadata reader behavior.
- [ ] Implement a minimal APP1 Exif/TIFF reader with no new dependencies.
- [ ] Store parsed capture time during `ProjectDatabase.ReplacePhotos`.
- [ ] Run focused metadata and database tests.
