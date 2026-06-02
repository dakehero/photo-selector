using PhotoSelector.Core.Exporting;
using PhotoSelector.Core.Projects;

namespace PhotoSelector.Tests;

public sealed class ExportServiceTests
{
    [Fact]
    public void Export_copies_paired_jpeg_and_raw_into_timestamped_directory_without_touching_sources()
    {
        using var tempDirectory = new TempDirectory();
        var sourceDirectory = Path.Combine(tempDirectory.Path, "source");
        var targetRoot = Path.Combine(tempDirectory.Path, "exports");
        Directory.CreateDirectory(sourceDirectory);
        var sourceJpg = Path.Combine(sourceDirectory, "IMG_0001.JPG");
        var sourceRaw = Path.Combine(sourceDirectory, "IMG_0001.CR3");
        File.WriteAllText(sourceJpg, "jpeg");
        File.WriteAllText(sourceRaw, "raw");
        var timestamp = new DateTimeOffset(2026, 6, 3, 12, 34, 56, TimeSpan.Zero);
        var photo = new PhotoItem(1, 1, "IMG_0001", sourceJpg, sourceRaw, null, "paired");

        var result = new ExportService().Export(new[] { photo }, targetRoot, timestamp);

        Assert.Equal(
            Path.Combine(targetRoot, "photo-selector-export-20260603-123456"),
            result.ExportDirectory);
        Assert.True(File.Exists(result.ExportedFiles.Single(path => path.EndsWith(".JPG"))));
        Assert.True(File.Exists(result.ExportedFiles.Single(path => path.EndsWith(".CR3"))));
        Assert.True(File.Exists(sourceJpg));
        Assert.True(File.Exists(sourceRaw));
        Assert.Contains("photo-selector-export-", Path.GetFileName(result.ExportDirectory));
    }

    [Fact]
    public void Export_copies_unpaired_existing_jpeg_only()
    {
        using var tempDirectory = new TempDirectory();
        var sourceDirectory = Path.Combine(tempDirectory.Path, "source");
        var targetRoot = Path.Combine(tempDirectory.Path, "exports");
        Directory.CreateDirectory(sourceDirectory);
        var sourceJpg = Path.Combine(sourceDirectory, "IMG_0002.JPG");
        File.WriteAllText(sourceJpg, "jpeg");
        var timestamp = new DateTimeOffset(2026, 6, 3, 12, 34, 56, TimeSpan.Zero);
        var photo = new PhotoItem(2, 1, "IMG_0002", sourceJpg, null, null, "unpaired");

        var result = new ExportService().Export(new[] { photo }, targetRoot, timestamp);

        var exportedFile = Assert.Single(result.ExportedFiles);
        Assert.Equal(Path.Combine(result.ExportDirectory, "IMG_0002.JPG"), exportedFile);
        Assert.True(File.Exists(exportedFile));
        Assert.True(File.Exists(sourceJpg));
    }

    [Fact]
    public void Export_suffixes_duplicate_file_names_from_different_source_directories()
    {
        using var tempDirectory = new TempDirectory();
        var firstSourceDirectory = Path.Combine(tempDirectory.Path, "source-1");
        var secondSourceDirectory = Path.Combine(tempDirectory.Path, "source-2");
        var targetRoot = Path.Combine(tempDirectory.Path, "exports");
        Directory.CreateDirectory(firstSourceDirectory);
        Directory.CreateDirectory(secondSourceDirectory);
        var firstSourceJpg = Path.Combine(firstSourceDirectory, "IMG_0001.JPG");
        var secondSourceJpg = Path.Combine(secondSourceDirectory, "IMG_0001.JPG");
        File.WriteAllText(firstSourceJpg, "first");
        File.WriteAllText(secondSourceJpg, "second");
        var timestamp = new DateTimeOffset(2026, 6, 3, 12, 34, 56, TimeSpan.Zero);
        var photos = new[]
        {
            new PhotoItem(1, 1, "IMG_0001", firstSourceJpg, null, null, "unpaired"),
            new PhotoItem(2, 1, "IMG_0001", secondSourceJpg, null, null, "unpaired"),
        };

        var result = new ExportService().Export(photos, targetRoot, timestamp);

        Assert.Equal(2, result.ExportedFiles.Count);
        Assert.Contains(Path.Combine(result.ExportDirectory, "IMG_0001.JPG"), result.ExportedFiles);
        Assert.Contains(Path.Combine(result.ExportDirectory, "IMG_0001-2.JPG"), result.ExportedFiles);
        Assert.Equal("first", File.ReadAllText(Path.Combine(result.ExportDirectory, "IMG_0001.JPG")));
        Assert.Equal("second", File.ReadAllText(Path.Combine(result.ExportDirectory, "IMG_0001-2.JPG")));
    }

    [Fact]
    public void Export_suffixes_destination_when_file_already_exists()
    {
        using var tempDirectory = new TempDirectory();
        var sourceDirectory = Path.Combine(tempDirectory.Path, "source");
        var targetRoot = Path.Combine(tempDirectory.Path, "exports");
        var exportDirectory = Path.Combine(targetRoot, "photo-selector-export-20260603-123456");
        Directory.CreateDirectory(sourceDirectory);
        Directory.CreateDirectory(exportDirectory);
        var sourceJpg = Path.Combine(sourceDirectory, "IMG_0001.JPG");
        var existingDestination = Path.Combine(exportDirectory, "IMG_0001.JPG");
        File.WriteAllText(sourceJpg, "new");
        File.WriteAllText(existingDestination, "existing");
        var timestamp = new DateTimeOffset(2026, 6, 3, 12, 34, 56, TimeSpan.Zero);
        var photo = new PhotoItem(1, 1, "IMG_0001", sourceJpg, null, null, "unpaired");

        var result = new ExportService().Export(new[] { photo }, targetRoot, timestamp);

        var exportedFile = Assert.Single(result.ExportedFiles);
        Assert.Equal(Path.Combine(exportDirectory, "IMG_0001-2.JPG"), exportedFile);
        Assert.Equal("existing", File.ReadAllText(existingDestination));
        Assert.Equal("new", File.ReadAllText(exportedFile));
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
