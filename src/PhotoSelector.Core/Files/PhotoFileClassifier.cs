namespace PhotoSelector.Core.Files;

public static class PhotoFileClassifier
{
    private static readonly HashSet<string> JpegExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
    };

    private static readonly HashSet<string> RawExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cr3",
        ".cr2",
        ".nef",
        ".arw",
        ".raf",
        ".rw2",
        ".dng",
        ".orf",
    };

    public static PhotoFileKind Classify(string path)
    {
        var extension = Path.GetExtension(path);

        if (JpegExtensions.Contains(extension))
        {
            return PhotoFileKind.Jpeg;
        }

        if (RawExtensions.Contains(extension))
        {
            return PhotoFileKind.Raw;
        }

        return PhotoFileKind.Unsupported;
    }
}
