using System.Globalization;
using PhotoSelector.Core.Projects;

namespace PhotoSelector.Core.Exporting;

public sealed class ExportService
{
    public ExportResult Export(IEnumerable<PhotoItem> photos, string targetRoot, DateTimeOffset timestamp)
    {
        var exportDirectory = Path.Combine(
            targetRoot,
            $"photo-selector-export-{timestamp.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}");
        Directory.CreateDirectory(exportDirectory);

        var exportedFiles = new List<string>();
        foreach (var photo in photos)
        {
            CopyIfPresent(photo.JpegPath, exportDirectory, exportedFiles);
            CopyIfPresent(photo.RawPath, exportDirectory, exportedFiles);
        }

        return new ExportResult(exportDirectory, exportedFiles);
    }

    private static void CopyIfPresent(string? sourcePath, string exportDirectory, ICollection<string> exportedFiles)
    {
        if (sourcePath is null || !File.Exists(sourcePath))
        {
            return;
        }

        var destinationPath = Path.Combine(exportDirectory, Path.GetFileName(sourcePath));
        File.Copy(sourcePath, destinationPath);
        exportedFiles.Add(destinationPath);
    }
}
