# Photo Selector MVP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the first working native-feeling photo selector MVP with JPG+RAW pairing, a shared SQLite catalog, queued AI rating jobs, catalog-first CLI commands, non-destructive export, and an Avalonia workbench shell.

**Architecture:** Use a .NET solution with focused projects: core storage/domain logic, AI integration, shared agent/workflow orchestration, CLI, Avalonia UI, shared config/credentials, and tests. Core behavior is implemented test-first and reused by CLI and GUI.

**Tech Stack:** .NET 10 currently, Avalonia, Microsoft.Data.Sqlite, xUnit, OpenAI .NET SDK for OpenAI where practical, and System.CommandLine for the NativeAOT-friendly CLI surface.

**2026-06-09 revision:** This plan has been corrected to match the current product design. Do not implement old database-path CLI commands. User-facing CLI commands must be catalog-first and should not expose SQLite paths.

---

## File Structure

- `PhotoSelector.sln`: solution root.
- `src/PhotoSelector.Core/`: domain models, scanner, pairing, SQLite repository, export service.
- `src/PhotoSelector.Ai/`: provider clients, scoring models, prompt contract, parser, and audit payload helpers.
- `src/PhotoSelector.Config/`: shared config paths, provider profiles, and credential resolution.
- `src/PhotoSelector.Agent/`: import workflow, queued rating jobs, and worker orchestration shared by CLI/GUI/future agents.
- `src/PhotoSelector.Cli/`: catalog-first commands: `pick`, `rate`, `coach`, `arena`, `scan`, `status`, `reset ratings`, `results`, `export`, `projects`, `open`, and `photos`.
- `src/PhotoSelector.App/`: Avalonia app matching the approved workbench prototype.
- `tests/PhotoSelector.Tests/`: xUnit tests for core and CLI-critical behavior.
- `prototypes/photo-selector-workbench.html`: already created visual reference for the desktop workbench.

---

### Task 1: Scaffold Solution

**Files:**
- Create: `PhotoSelector.sln`
- Create: `src/PhotoSelector.Core/PhotoSelector.Core.csproj`
- Create: `src/PhotoSelector.Ai/PhotoSelector.Ai.csproj`
- Create: `src/PhotoSelector.Config/PhotoSelector.Config.csproj`
- Create: `src/PhotoSelector.Agent/PhotoSelector.Agent.csproj`
- Create: `src/PhotoSelector.Cli/PhotoSelector.Cli.csproj`
- Create: `src/PhotoSelector.App/PhotoSelector.App.csproj`
- Create: `tests/PhotoSelector.Tests/PhotoSelector.Tests.csproj`

- [ ] **Step 1: Create the .NET solution and projects**

Run:

```powershell
dotnet new sln -n PhotoSelector
dotnet new classlib -n PhotoSelector.Core -o src/PhotoSelector.Core
dotnet new classlib -n PhotoSelector.Ai -o src/PhotoSelector.Ai
dotnet new classlib -n PhotoSelector.Config -o src/PhotoSelector.Config
dotnet new classlib -n PhotoSelector.Agent -o src/PhotoSelector.Agent
dotnet new console -n PhotoSelector.Cli -o src/PhotoSelector.Cli
dotnet new avalonia.app -n PhotoSelector.App -o src/PhotoSelector.App
dotnet new xunit -n PhotoSelector.Tests -o tests/PhotoSelector.Tests
dotnet sln add src/PhotoSelector.Core/PhotoSelector.Core.csproj
dotnet sln add src/PhotoSelector.Ai/PhotoSelector.Ai.csproj
dotnet sln add src/PhotoSelector.Config/PhotoSelector.Config.csproj
dotnet sln add src/PhotoSelector.Agent/PhotoSelector.Agent.csproj
dotnet sln add src/PhotoSelector.Cli/PhotoSelector.Cli.csproj
dotnet sln add src/PhotoSelector.App/PhotoSelector.App.csproj
dotnet sln add tests/PhotoSelector.Tests/PhotoSelector.Tests.csproj
```

Expected: solution contains Core, AI, Config, Agent, CLI, App, and Tests projects.

- [ ] **Step 2: Add references and packages**

Run:

```powershell
dotnet add src/PhotoSelector.Ai/PhotoSelector.Ai.csproj reference src/PhotoSelector.Core/PhotoSelector.Core.csproj
dotnet add src/PhotoSelector.Agent/PhotoSelector.Agent.csproj reference src/PhotoSelector.Core/PhotoSelector.Core.csproj
dotnet add src/PhotoSelector.Agent/PhotoSelector.Agent.csproj reference src/PhotoSelector.Ai/PhotoSelector.Ai.csproj
dotnet add src/PhotoSelector.Agent/PhotoSelector.Agent.csproj reference src/PhotoSelector.Config/PhotoSelector.Config.csproj
dotnet add src/PhotoSelector.Cli/PhotoSelector.Cli.csproj reference src/PhotoSelector.Agent/PhotoSelector.Agent.csproj
dotnet add src/PhotoSelector.Cli/PhotoSelector.Cli.csproj reference src/PhotoSelector.Core/PhotoSelector.Core.csproj
dotnet add src/PhotoSelector.Cli/PhotoSelector.Cli.csproj reference src/PhotoSelector.Ai/PhotoSelector.Ai.csproj
dotnet add src/PhotoSelector.Cli/PhotoSelector.Cli.csproj reference src/PhotoSelector.Config/PhotoSelector.Config.csproj
dotnet add src/PhotoSelector.App/PhotoSelector.App.csproj reference src/PhotoSelector.Core/PhotoSelector.Core.csproj
dotnet add src/PhotoSelector.App/PhotoSelector.App.csproj reference src/PhotoSelector.Ai/PhotoSelector.Ai.csproj
dotnet add src/PhotoSelector.App/PhotoSelector.App.csproj reference src/PhotoSelector.Config/PhotoSelector.Config.csproj
dotnet add tests/PhotoSelector.Tests/PhotoSelector.Tests.csproj reference src/PhotoSelector.Core/PhotoSelector.Core.csproj
dotnet add tests/PhotoSelector.Tests/PhotoSelector.Tests.csproj reference src/PhotoSelector.Ai/PhotoSelector.Ai.csproj
dotnet add tests/PhotoSelector.Tests/PhotoSelector.Tests.csproj reference src/PhotoSelector.Agent/PhotoSelector.Agent.csproj
dotnet add tests/PhotoSelector.Tests/PhotoSelector.Tests.csproj reference src/PhotoSelector.Config/PhotoSelector.Config.csproj
dotnet add src/PhotoSelector.Core/PhotoSelector.Core.csproj package Microsoft.Data.Sqlite
```

Expected: restore succeeds.

- [ ] **Step 3: Build**

Run:

```powershell
dotnet build
```

Expected: build succeeds.

- [ ] **Step 4: Commit**

```powershell
git add PhotoSelector.sln src tests
git commit -m "chore: scaffold photo selector solution"
```

---

### Task 2: File Type Detection And JPG+RAW Pairing

**Files:**
- Create: `src/PhotoSelector.Core/Files/PhotoFileKind.cs`
- Create: `src/PhotoSelector.Core/Files/PhotoFileClassifier.cs`
- Create: `src/PhotoSelector.Core/Scanning/PhotoPair.cs`
- Create: `src/PhotoSelector.Core/Scanning/PhotoScanner.cs`
- Create: `tests/PhotoSelector.Tests/PhotoScannerTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/PhotoSelector.Tests/PhotoScannerTests.cs`:

```csharp
using PhotoSelector.Core.Files;
using PhotoSelector.Core.Scanning;

namespace PhotoSelector.Tests;

public sealed class PhotoScannerTests
{
    [Theory]
    [InlineData("IMG_0001.JPG", PhotoFileKind.Jpeg)]
    [InlineData("IMG_0001.jpeg", PhotoFileKind.Jpeg)]
    [InlineData("IMG_0001.CR3", PhotoFileKind.Raw)]
    [InlineData("IMG_0001.nef", PhotoFileKind.Raw)]
    [InlineData("notes.txt", PhotoFileKind.Unsupported)]
    public void Classifies_supported_photo_extensions_case_insensitively(string fileName, PhotoFileKind expected)
    {
        Assert.Equal(expected, PhotoFileClassifier.Classify(fileName));
    }

    [Fact]
    public void Pairs_jpg_and_raw_files_with_the_same_stem()
    {
        var files = new[]
        {
            Path.Combine("shoot", "IMG_0001.JPG"),
            Path.Combine("shoot", "IMG_0001.CR3"),
            Path.Combine("shoot", "IMG_0002.JPG")
        };

        var pairs = PhotoScanner.ScanFiles(files).OrderBy(p => p.BaseName).ToList();

        Assert.Equal(2, pairs.Count);
        Assert.Equal("IMG_0001", pairs[0].BaseName);
        Assert.EndsWith("IMG_0001.JPG", pairs[0].JpegPath);
        Assert.EndsWith("IMG_0001.CR3", pairs[0].RawPath);
        Assert.Equal("IMG_0002", pairs[1].BaseName);
        Assert.EndsWith("IMG_0002.JPG", pairs[1].JpegPath);
        Assert.Null(pairs[1].RawPath);
    }
}
```

- [ ] **Step 2: Verify tests fail**

Run:

```powershell
dotnet test tests/PhotoSelector.Tests/PhotoSelector.Tests.csproj --filter PhotoScannerTests
```

Expected: FAIL because classifier and scanner types do not exist.

- [ ] **Step 3: Implement minimal core scanner**

Create the files named above with these public APIs:

```csharp
namespace PhotoSelector.Core.Files;

public enum PhotoFileKind
{
    Unsupported,
    Jpeg,
    Raw
}
```

```csharp
namespace PhotoSelector.Core.Files;

public static class PhotoFileClassifier
{
    private static readonly HashSet<string> JpegExtensions = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg" };
    private static readonly HashSet<string> RawExtensions = new(StringComparer.OrdinalIgnoreCase) { ".cr3", ".cr2", ".nef", ".arw", ".raf", ".rw2", ".dng", ".orf" };

    public static PhotoFileKind Classify(string path)
    {
        var extension = Path.GetExtension(path);
        if (JpegExtensions.Contains(extension)) return PhotoFileKind.Jpeg;
        if (RawExtensions.Contains(extension)) return PhotoFileKind.Raw;
        return PhotoFileKind.Unsupported;
    }
}
```

```csharp
namespace PhotoSelector.Core.Scanning;

public sealed record PhotoPair(string BaseName, string? JpegPath, string? RawPath);
```

```csharp
using PhotoSelector.Core.Files;

namespace PhotoSelector.Core.Scanning;

public static class PhotoScanner
{
    public static IReadOnlyList<PhotoPair> ScanFiles(IEnumerable<string> files)
    {
        var byStem = new Dictionary<string, (string? Jpeg, string? Raw)>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            var kind = PhotoFileClassifier.Classify(file);
            if (kind == PhotoFileKind.Unsupported) continue;

            var stem = Path.GetFileNameWithoutExtension(file);
            byStem.TryGetValue(stem, out var existing);
            byStem[stem] = kind == PhotoFileKind.Jpeg
                ? (file, existing.Raw)
                : (existing.Jpeg, file);
        }

        return byStem
            .Select(item => new PhotoPair(item.Key, item.Value.Jpeg, item.Value.Raw))
            .OrderBy(pair => pair.BaseName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IReadOnlyList<PhotoPair> ScanDirectory(string directory)
    {
        return ScanFiles(Directory.EnumerateFiles(directory));
    }
}
```

- [ ] **Step 4: Verify tests pass**

Run:

```powershell
dotnet test tests/PhotoSelector.Tests/PhotoSelector.Tests.csproj --filter PhotoScannerTests
```

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/PhotoSelector.Core tests/PhotoSelector.Tests
git commit -m "feat: add photo scanning and pairing"
```

---

### Task 3: SQLite Project Store

**Files:**
- Create: `src/PhotoSelector.Core/Projects/PhotoProject.cs`
- Create: `src/PhotoSelector.Core/Projects/PhotoItem.cs`
- Create: `src/PhotoSelector.Core/Storage/ProjectDatabase.cs`
- Create: `tests/PhotoSelector.Tests/ProjectDatabaseTests.cs`

- [ ] **Step 1: Write failing tests**

Create a test that opens a temp SQLite database, migrates schema, creates a project from scanned pairs, and reads it back.

Expected assertions:

```csharp
Assert.Single(projects);
Assert.Equal(sourceDirectory, projects[0].SourceDirectory);
Assert.Equal(2, photos.Count);
Assert.Equal("IMG_0001", photos[0].BaseName);
Assert.NotNull(photos[0].JpegPath);
Assert.NotNull(photos[0].RawPath);
```

- [ ] **Step 2: Verify tests fail**

Run:

```powershell
dotnet test tests/PhotoSelector.Tests/PhotoSelector.Tests.csproj --filter ProjectDatabaseTests
```

Expected: FAIL because database types do not exist.

- [ ] **Step 3: Implement database schema and repository**

Implement `ProjectDatabase` with:

```csharp
public static ProjectDatabase Open(string databasePath);
public void Migrate();
public long CreateProject(string sourceDirectory);
public void ReplacePhotos(long projectId, IEnumerable<PhotoPair> pairs);
public IReadOnlyList<PhotoProject> ListProjects();
public IReadOnlyList<PhotoItem> ListPhotos(long projectId);
```

Use tables `schema_version`, `projects`, and `photos` first. Add the remaining rating/export tables in later tasks when behavior needs them.

- [ ] **Step 4: Verify tests pass**

Run:

```powershell
dotnet test tests/PhotoSelector.Tests/PhotoSelector.Tests.csproj --filter ProjectDatabaseTests
```

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/PhotoSelector.Core tests/PhotoSelector.Tests
git commit -m "feat: add sqlite project storage"
```

---

### Task 4: Non-Destructive Export

**Files:**
- Create: `src/PhotoSelector.Core/Exporting/ExportService.cs`
- Create: `src/PhotoSelector.Core/Exporting/ExportResult.cs`
- Create: `tests/PhotoSelector.Tests/ExportServiceTests.cs`

- [ ] **Step 1: Write failing tests**

Test that exporting a paired photo copies both files into a timestamped export subdirectory and leaves source files untouched.

Expected assertions:

```csharp
Assert.True(File.Exists(result.ExportedFiles.Single(path => path.EndsWith(".JPG"))));
Assert.True(File.Exists(result.ExportedFiles.Single(path => path.EndsWith(".CR3"))));
Assert.True(File.Exists(sourceJpg));
Assert.True(File.Exists(sourceRaw));
Assert.Contains("photo-selector-export-", Path.GetFileName(result.ExportDirectory));
```

- [ ] **Step 2: Verify tests fail**

Run:

```powershell
dotnet test tests/PhotoSelector.Tests/PhotoSelector.Tests.csproj --filter ExportServiceTests
```

Expected: FAIL because export service does not exist.

- [ ] **Step 3: Implement export service**

Implement:

```csharp
public sealed class ExportService
{
    public ExportResult Export(IEnumerable<PhotoItem> photos, string targetRoot, DateTimeOffset timestamp);
}
```

Use directory name format `photo-selector-export-yyyyMMdd-HHmmss`. Copy non-null JPG and RAW paths. Never delete or move source files.

- [ ] **Step 4: Verify tests pass**

Run:

```powershell
dotnet test tests/PhotoSelector.Tests/PhotoSelector.Tests.csproj --filter ExportServiceTests
```

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/PhotoSelector.Core tests/PhotoSelector.Tests
git commit -m "feat: add non destructive export"
```

---

### Task 5: AI Result Parsing

**Files:**
- Create: `src/PhotoSelector.Ai/Ratings/AiRating.cs`
- Create: `src/PhotoSelector.Ai/Ratings/AiRatingParser.cs`
- Create: `tests/PhotoSelector.Tests/AiRatingParserTests.cs`

- [ ] **Step 1: Write failing tests**

Test valid JSON parses into `photo_type`, one-decimal `score`, `category`, `criteria`, and `reason`. Invalid score format, out-of-range scores, invalid criteria scores, or invalid category returns a validation failure.

- [ ] **Step 2: Verify tests fail**

Run:

```powershell
dotnet test tests/PhotoSelector.Tests/PhotoSelector.Tests.csproj --filter AiRatingParserTests
```

Expected: FAIL because parser does not exist.

- [ ] **Step 3: Implement parser**

Implement a parser that accepts:

```json
{"photo_type":"street","score":8.4,"category":"keep","criteria":[{"name":"impact","score":8.5,"comment":"strong moment"}],"reason":"sharp subject"}
```

and validates score is `1.0` through `10.0` with exactly one decimal place, each criterion score follows the same rule, and category is `keep`, `maybe`, or `reject`.

- [ ] **Step 4: Verify tests pass**

Run:

```powershell
dotnet test tests/PhotoSelector.Tests/PhotoSelector.Tests.csproj --filter AiRatingParserTests
```

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/PhotoSelector.Ai tests/PhotoSelector.Tests
git commit -m "feat: parse ai rating results"
```

---

### Task 6: Catalog-First CLI And Rating Jobs

**Files:**
- Modify: `src/PhotoSelector.Cli/Program.cs`
- Modify: `src/PhotoSelector.Core/Storage/ProjectDatabase.cs`
- Create: `src/PhotoSelector.Core/Projects/RatingJob.cs`
- Create: `src/PhotoSelector.Core/Projects/RatingJobSummary.cs`
- Create: `src/PhotoSelector.Agent/Workflows/ImportWorkflow.cs`
- Create: `src/PhotoSelector.Agent/Workers/RatingWorker.cs`
- Create: `tests/PhotoSelector.Tests/CliSmokeTests.cs`
- Create/modify: `tests/PhotoSelector.Tests/CliRateTests.cs`

- [ ] **Step 1: Write failing smoke tests**

Test the CLI can:

- `pick <directory>` import/update a directory, rate pending or failed photos with the selection prompt, and output ranked results.
- `rate <image>` rate one image with the rating prompt.
- `coach <image>` critique one image with the coaching prompt.
- `arena <directory> --models <model-a,model-b>` compare models and save arena runs.
- `scan <directory>` synchronously import and rate pending jobs.
- `status [directory]` report pending/rated/failed counts.
- `reset ratings <directory>` delete ratings, preserve audit logs, and requeue work.
- `results [directory]` summarize rating coverage, keep/maybe/reject counts, and top candidates.
- `results [directory] --photo <photo-id|base-name> --audit --json` emit a parseable decision trace.
- `export <keep|maybe|reject> <directory> <target>` copy matching JPG+RAW pairs without requiring SQLite paths.
- `projects list --json`, `open <project-id|directory> --json`, and `photos list --project <id> --json` emit parseable JSON.

- [ ] **Step 2: Verify tests fail**

Run:

```powershell
dotnet test tests/PhotoSelector.Tests/PhotoSelector.Tests.csproj --filter CliSmokeTests
```

Expected: FAIL because CLI commands are not implemented.

- [ ] **Step 3: Implement CLI**

Implement commands:

```powershell
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
photo-selector export <keep|maybe|reject> <directory> <target>
photo-selector projects list --json
photo-selector open <project-id|directory> --json
photo-selector photos list --project <project-id> --json
```

Do not implement `rate <project-db>`, `list <project-db>`, `export <project-db>`, `audit <project-db>`, or `quick-select`. The product has not shipped; there is no compatibility requirement.

Do not reintroduce user-facing `import`, `process`, `flush`, or `worker` commands. Product commands should express the user's intent directly.

`pick` should create/update the shared catalog, process pending or failed jobs with the selection prompt, and return ranked results.

`rate` and `coach` should process one image with separate prompt contracts.

`arena` should compare multiple models on the same photo set and persist the comparison for `arena list` and `arena show`.

`scan` should create/update the shared catalog and synchronously process rating jobs for that directory before returning.

`reset ratings` should delete rating outputs and preserve audit logs unless `--with-audit` is supplied.

`results` should summarize rating coverage, keep/maybe/reject counts, and top candidates for all projects or one directory.

`results --photo <photo-id|base-name> --audit` should expose one photo's redacted request, raw model message, raw provider response, status, and parse error when present.

`export` should copy JPG+RAW pairs whose latest AI rating matches the requested category into a timestamped export directory under the target root.

- [ ] **Step 4: Verify tests pass**

Run:

```powershell
dotnet test tests/PhotoSelector.Tests/PhotoSelector.Tests.csproj --filter CliSmokeTests
```

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/PhotoSelector.Cli tests/PhotoSelector.Tests
git commit -m "feat: add photo selector cli"
```

---

### Task 7: Avalonia Workbench Shell

**Files:**
- Modify: `src/PhotoSelector.App/MainWindow.axaml`
- Modify: `src/PhotoSelector.App/MainWindow.axaml.cs`
- Create: `src/PhotoSelector.App/ViewModels/MainWindowViewModel.cs`

- [ ] **Step 1: Create shell matching prototype**

Build the first Avalonia screen with:

- top toolbar
- left project/filter panel
- center thumbnail grid placeholder
- right details panel
- keep/maybe/reject and star controls

Use the prototype file as visual reference: `prototypes/photo-selector-workbench.html`.

- [ ] **Step 2: Wire sample state**

Create sample in-memory rows so the app opens with a visible workbench before real folder picking is connected.

- [ ] **Step 3: Run the app**

Run:

```powershell
dotnet run --project src/PhotoSelector.App/PhotoSelector.App.csproj
```

Expected: Avalonia window opens and resembles the approved prototype.

- [ ] **Step 4: Commit**

```powershell
git add src/PhotoSelector.App
git commit -m "feat: add avalonia workbench shell"
```

---

### Task 8: Wire GUI To Core Scan Flow

**Files:**
- Modify: `src/PhotoSelector.App/ViewModels/MainWindowViewModel.cs`
- Modify: `src/PhotoSelector.App/MainWindow.axaml.cs`

- [ ] **Step 1: Add open-directory and scan action**

Use Avalonia storage picker to choose a directory, scan it with `PhotoScanner.ScanDirectory`, and populate the grid rows.

- [ ] **Step 2: Persist scanned project**

Create or update the shared catalog at `ConfigPaths.GetDatabasePath()`. Do not create the default SQLite database in the selected photo directory. GUI scan/import behavior should call shared workflow code instead of duplicating CLI logic.

- [ ] **Step 3: Verify manually**

Run:

```powershell
dotnet run --project src/PhotoSelector.App/PhotoSelector.App.csproj
```

Expected: selecting a directory with sample JPG/RAW files populates the grid and pair counts.

- [ ] **Step 4: Commit**

```powershell
git add src/PhotoSelector.App
git commit -m "feat: wire app scan workflow"
```

---

## Self-Review Checklist

- Spec coverage: scanning, JPG+RAW pairing, shared SQLite catalog, rating jobs, Agent workflows, CLI, Avalonia workbench, AI providers, and AI JSON parsing are covered.
- Known gaps for follow-up work should be tracked in Superpowers specs/plans under `docs/superpowers/`, not in a root `TODO.md`.
- Placeholder scan: no `TBD` or unspecified implementation placeholders are intended.
- Type consistency: `PhotoPair`, `PhotoItem`, `ProjectDatabase`, `RatingJob`, `ImportWorkflow`, `RatingWorker`, `ExportService`, and `AiRatingParser` names are used consistently across tasks.
