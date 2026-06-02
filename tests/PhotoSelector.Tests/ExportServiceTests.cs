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
