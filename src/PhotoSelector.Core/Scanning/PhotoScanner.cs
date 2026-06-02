using PhotoSelector.Core.Files;

namespace PhotoSelector.Core.Scanning;

public static class PhotoScanner
{
    public static IReadOnlyList<PhotoPair> ScanFiles(IEnumerable<string> files)
    {
        var byBaseName = new Dictionary<string, (string? JpegPath, string? RawPath)>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file, StringComparer.Ordinal))
        {
            var kind = PhotoFileClassifier.Classify(file);
            if (kind == PhotoFileKind.Unsupported)
            {
                continue;
            }

            var baseName = Path.GetFileNameWithoutExtension(file);
            byBaseName.TryGetValue(baseName, out var paths);

            paths = kind == PhotoFileKind.Jpeg
                ? (paths.JpegPath ?? file, paths.RawPath)
                : (paths.JpegPath, paths.RawPath ?? file);

            byBaseName[baseName] = paths;
        }

        return byBaseName
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new PhotoPair(pair.Key, pair.Value.JpegPath, pair.Value.RawPath))
            .ToList();
    }

    public static IReadOnlyList<PhotoPair> ScanDirectory(string directory)
    {
        return ScanFiles(Directory.EnumerateFiles(directory));
    }
}
