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
        var usedDestinationPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var photo in photos)
        {
            CopyIfPresent(photo.JpegPath, exportDirectory, exportedFiles, usedDestinationPaths);
            CopyIfPresent(photo.RawPath, exportDirectory, exportedFiles, usedDestinationPaths);
        }

        return new ExportResult(exportDirectory, exportedFiles);
    }

    private static void CopyIfPresent(
        string? sourcePath,
        string exportDirectory,
        ICollection<string> exportedFiles,
        ISet<string> usedDestinationPaths)
    {
        if (sourcePath is null || !File.Exists(sourcePath))
        {
            return;
        }

        var destinationPath = ResolveDestinationPath(sourcePath, exportDirectory, usedDestinationPaths);
        File.Copy(sourcePath, destinationPath);
        usedDestinationPaths.Add(destinationPath);
        exportedFiles.Add(destinationPath);
    }

    private static string ResolveDestinationPath(
        string sourcePath,
        string exportDirectory,
        ISet<string> usedDestinationPaths)
    {
        var fileName = Path.GetFileNameWithoutExtension(sourcePath);
        var extension = Path.GetExtension(sourcePath);
        var destinationPath = Path.Combine(exportDirectory, $"{fileName}{extension}");
        var suffix = 2;

        while (File.Exists(destinationPath) || usedDestinationPaths.Contains(destinationPath))
        {
            destinationPath = Path.Combine(exportDirectory, $"{fileName}-{suffix}{extension}");
            suffix++;
        }

        return destinationPath;
    }
}
