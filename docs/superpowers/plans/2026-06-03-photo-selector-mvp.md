# Photo Selector MVP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the first working native-feeling photo selector MVP with JPG+RAW pairing, SQLite project state, non-destructive export, CLI commands, and an Avalonia workbench shell.

**Architecture:** Use a .NET solution with focused projects: core domain logic, AI integration, CLI, Avalonia UI, and tests. Core behavior is implemented test-first and reused by CLI and GUI.

**Tech Stack:** .NET 8 or newer, Avalonia, Microsoft.Data.Sqlite, xUnit, System.CommandLine or Cocona after confirming availability.

---

## File Structure

- `PhotoSelector.sln`: solution root.
- `src/PhotoSelector.Core/`: domain models, scanner, pairing, SQLite repository, export service.
- `src/PhotoSelector.Ai/`: OpenAI-compatible scoring models and parser; real HTTP client can follow after core MVP.
- `src/PhotoSelector.Cli/`: `scan`, `list`, `export`, and placeholder `rate` commands.
- `src/PhotoSelector.App/`: Avalonia app matching the approved workbench prototype.
- `tests/PhotoSelector.Tests/`: xUnit tests for core and CLI-critical behavior.
- `prototypes/photo-selector-workbench.html`: already created visual reference for the desktop workbench.

---

### Task 1: Scaffold Solution

**Files:**
- Create: `PhotoSelector.sln`
- Create: `src/PhotoSelector.Core/PhotoSelector.Core.csproj`
- Create: `src/PhotoSelector.Ai/PhotoSelector.Ai.csproj`
- Create: `src/PhotoSelector.Cli/PhotoSelector.Cli.csproj`
- Create: `src/PhotoSelector.App/PhotoSelector.App.csproj`
- Create: `tests/PhotoSelector.Tests/PhotoSelector.Tests.csproj`

- [ ] **Step 1: Create the .NET solution and projects**

Run:

```powershell
dotnet new sln -n PhotoSelector
dotnet new classlib -n PhotoSelector.Core -o src/PhotoSelector.Core
dotnet new classlib -n PhotoSelector.Ai -o src/PhotoSelector.Ai
dotnet new console -n PhotoSelector.Cli -o src/PhotoSelector.Cli
dotnet new avalonia.app -n PhotoSelector.App -o src/PhotoSelector.App
dotnet new xunit -n PhotoSelector.Tests -o tests/PhotoSelector.Tests
dotnet sln add src/PhotoSelector.Core/PhotoSelector.Core.csproj
dotnet sln add src/PhotoSelector.Ai/PhotoSelector.Ai.csproj
dotnet sln add src/PhotoSelector.Cli/PhotoSelector.Cli.csproj
dotnet sln add src/PhotoSelector.App/PhotoSelector.App.csproj
dotnet sln add tests/PhotoSelector.Tests/PhotoSelector.Tests.csproj
```

Expected: solution contains all five projects.

- [ ] **Step 2: Add references and packages**

Run:

```powershell
dotnet add src/PhotoSelector.Ai/PhotoSelector.Ai.csproj reference src/PhotoSelector.Core/PhotoSelector.Core.csproj
dotnet add src/PhotoSelector.Cli/PhotoSelector.Cli.csproj reference src/PhotoSelector.Core/PhotoSelector.Core.csproj
dotnet add src/PhotoSelector.Cli/PhotoSelector.Cli.csproj reference src/PhotoSelector.Ai/PhotoSelector.Ai.csproj
dotnet add src/PhotoSelector.App/PhotoSelector.App.csproj reference src/PhotoSelector.Core/PhotoSelector.Core.csproj
dotnet add src/PhotoSelector.App/PhotoSelector.App.csproj reference src/PhotoSelector.Ai/PhotoSelector.Ai.csproj
dotnet add tests/PhotoSelector.Tests/PhotoSelector.Tests.csproj reference src/PhotoSelector.Core/PhotoSelector.Core.csproj
dotnet add tests/PhotoSelector.Tests/PhotoSelector.Tests.csproj reference src/PhotoSelector.Ai/PhotoSelector.Ai.csproj
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

Test valid JSON parses into score/category/reason and invalid score/category returns a validation failure.

- [ ] **Step 2: Verify tests fail**

Run:

```powershell
dotnet test tests/PhotoSelector.Tests/PhotoSelector.Tests.csproj --filter AiRatingParserTests
```

Expected: FAIL because parser does not exist.

- [ ] **Step 3: Implement parser**

Implement a parser that accepts:

```json
{"score":4,"category":"keep","reason":"sharp subject"}
```

and validates score is 1 through 5 and category is `keep`, `maybe`, or `reject`.

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

### Task 6: CLI Scan, List, Export

**Files:**
- Modify: `src/PhotoSelector.Cli/Program.cs`
- Create: `tests/PhotoSelector.Tests/CliSmokeTests.cs`

- [ ] **Step 1: Write failing smoke tests**

Test the CLI can scan a temp folder, list JSON, and export files by invoking the built executable or calling command handlers directly.

- [ ] **Step 2: Verify tests fail**

Run:

```powershell
dotnet test tests/PhotoSelector.Tests/PhotoSelector.Tests.csproj --filter CliSmokeTests
```

Expected: FAIL because CLI commands are not implemented.

- [ ] **Step 3: Implement CLI**

Implement commands:

```powershell
photo-selector scan <directory>
photo-selector list <project-db> --json
photo-selector export <project-db> --category keep --out <directory>
photo-selector rate <project-db> --provider openai-compatible
```

For the MVP, `rate` can print a clear message that API scoring will be wired after UI/core flow, but the command shape must exist.

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

Create or update a SQLite database in the selected directory as `.photo-selector/photo-selector.db`.

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

- Spec coverage: scanning, JPG+RAW pairing, SQLite, non-destructive export, CLI, Avalonia workbench, and AI JSON parsing are covered.
- Known gap: real OpenAI-compatible HTTP calls are intentionally after MVP shell and parser because they need API settings UI and live network verification.
- Placeholder scan: no `TBD` or unspecified implementation placeholders are intended.
- Type consistency: `PhotoPair`, `PhotoItem`, `ProjectDatabase`, `ExportService`, and `AiRatingParser` names are used consistently across tasks.
