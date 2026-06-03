using System.Text.Json;
using PhotoSelector.Cli;
using PhotoSelector.Core.Storage;

namespace PhotoSelector.Tests;

public sealed class CliSmokeTests
{
    [Fact]
    public void Scan_creates_project_database_from_directory_with_jpg_and_raw_files()
    {
        using var tempDirectory = new TempDirectory();
        var sourceDirectory = Path.Combine(tempDirectory.Path, "shoot");
        Directory.CreateDirectory(sourceDirectory);
        var jpegPath = Path.Combine(sourceDirectory, "IMG_0001.JPG");
        var rawPath = Path.Combine(sourceDirectory, "IMG_0001.CR3");
        File.WriteAllText(jpegPath, "jpeg");
        File.WriteAllText(rawPath, "raw");

        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = CliApp.Run(new[] { "scan", sourceDirectory }, output, error);

        var databasePath = Path.Combine(sourceDirectory, ".photo-selector", "photo-selector.db");
        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(databasePath));
        Assert.Contains(databasePath, output.ToString());
        Assert.Equal(string.Empty, error.ToString());

        using var database = ProjectDatabase.Open(databasePath);
        var project = Assert.Single(database.ListProjects());
        Assert.Equal(sourceDirectory, project.SourceDirectory);
        var photo = Assert.Single(database.ListPhotos(project.Id));
        Assert.Equal("IMG_0001", photo.BaseName);
        Assert.Equal(jpegPath, photo.JpegPath);
        Assert.Equal(rawPath, photo.RawPath);
    }

    [Fact]
    public void List_json_emits_parseable_projects_and_photos()
    {
        using var tempDirectory = new TempDirectory();
        var sourceDirectory = CreateScannedSource(tempDirectory.Path);
        var databasePath = Path.Combine(sourceDirectory, ".photo-selector", "photo-selector.db");

        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = CliApp.Run(new[] { "list", databasePath, "--json" }, output, error);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        using var document = JsonDocument.Parse(output.ToString());
        var project = Assert.Single(document.RootElement.GetProperty("projects").EnumerateArray());
        Assert.Equal(sourceDirectory, project.GetProperty("sourceDirectory").GetString());
        var photos = project.GetProperty("photos").EnumerateArray().ToList();
        Assert.Equal(2, photos.Count);
        Assert.Equal("IMG_0001", photos[0].GetProperty("baseName").GetString());
        Assert.Equal(Path.Combine(sourceDirectory, "IMG_0001.JPG"), photos[0].GetProperty("jpegPath").GetString());
        Assert.Equal(Path.Combine(sourceDirectory, "IMG_0001.CR3"), photos[0].GetProperty("rawPath").GetString());
        Assert.Equal("IMG_0002", photos[1].GetProperty("baseName").GetString());
        Assert.Equal(Path.Combine(sourceDirectory, "IMG_0002.JPG"), photos[1].GetProperty("jpegPath").GetString());
        Assert.True(photos[1].GetProperty("rawPath").ValueKind is JsonValueKind.Null);
    }

    [Fact]
    public void Export_copies_all_scanned_photo_files_for_mvp_keep_category()
    {
        using var tempDirectory = new TempDirectory();
        var sourceDirectory = CreateScannedSource(tempDirectory.Path);
        var databasePath = Path.Combine(sourceDirectory, ".photo-selector", "photo-selector.db");
        var exportRoot = Path.Combine(tempDirectory.Path, "exports");

        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = CliApp.Run(
            new[] { "export", databasePath, "--category", "keep", "--out", exportRoot },
            output,
            error);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Contains("all photos", output.ToString(), StringComparison.OrdinalIgnoreCase);
        var exportDirectory = Assert.Single(Directory.EnumerateDirectories(exportRoot));
        Assert.True(File.Exists(Path.Combine(exportDirectory, "IMG_0001.JPG")));
        Assert.True(File.Exists(Path.Combine(exportDirectory, "IMG_0001.CR3")));
        Assert.True(File.Exists(Path.Combine(exportDirectory, "IMG_0002.JPG")));
    }

    [Fact]
    public void Rate_openai_compatible_provider_is_a_successful_placeholder()
    {
        using var tempDirectory = new TempDirectory();
        var databasePath = Path.Combine(tempDirectory.Path, "photo-selector.db");
        using (var database = ProjectDatabase.Open(databasePath))
        {
            database.Migrate();
        }

        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = CliApp.Run(
            new[] { "rate", databasePath, "--provider", "openai-compatible" },
            output,
            error);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        Assert.Contains("not", output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("wired", output.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateScannedSource(string rootDirectory)
    {
        var sourceDirectory = Path.Combine(rootDirectory, Path.GetRandomFileName());
        Directory.CreateDirectory(sourceDirectory);
        File.WriteAllText(Path.Combine(sourceDirectory, "IMG_0001.JPG"), "jpeg");
        File.WriteAllText(Path.Combine(sourceDirectory, "IMG_0001.CR3"), "raw");
        File.WriteAllText(Path.Combine(sourceDirectory, "IMG_0002.JPG"), "jpeg");

        var exitCode = CliApp.Run(
            new[] { "scan", sourceDirectory },
            TextWriter.Null,
            TextWriter.Null);
        Assert.Equal(0, exitCode);

        return sourceDirectory;
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName());
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
