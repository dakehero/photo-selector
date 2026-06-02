using PhotoSelector.Core.Files;

namespace PhotoSelector.Core.Scanning;

public static class PhotoScanner
{
    public static IReadOnlyList<PhotoPair> ScanFiles(IEnumerable<string> files)
    {
        var byBaseName = new Dictionary<string, (string? JpegPath, string? RawPath)>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            var kind = PhotoFileClassifier.Classify(file);
            if (kind == PhotoFileKind.Unsupported)
            {
                continue;
            }

            var baseName = Path.GetFileNameWithoutExtension(file);
            byBaseName.TryGetValue(baseName, out var paths);

            paths = kind == PhotoFileKind.Jpeg
                ? (file, paths.RawPath)
                : (paths.JpegPath, file);

            byBaseName[baseName] = paths;
        }

        return byBaseName
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new PhotoPair(pair.Key, pair.Value.JpegPath, pair.Value.RawPath))
            .ToList();
    }

    public static IReadOnlyList<PhotoPair> ScanDirectory(string directory)
    {
        return ScanFiles(Directory.EnumerateFiles(directory));
    }
}
