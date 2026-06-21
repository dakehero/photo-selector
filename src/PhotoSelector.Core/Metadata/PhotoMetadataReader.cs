using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace PhotoSelector.Core.Metadata;

public static class PhotoMetadataReader
{
    public static DateTimeOffset? ReadCaptureTime(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            return null;
        }

        try
        {
            var directories = ImageMetadataReader.ReadMetadata(imagePath);
            var exif = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            var captureTime = exif?.GetDateTime(ExifDirectoryBase.TagDateTimeOriginal);
            return captureTime is null
                ? null
                : new DateTimeOffset(DateTime.SpecifyKind(captureTime.Value, DateTimeKind.Unspecified), TimeSpan.Zero);
        }
        catch (ImageProcessingException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
