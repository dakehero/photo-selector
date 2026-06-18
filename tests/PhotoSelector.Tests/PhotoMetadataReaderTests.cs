using PhotoSelector.Core.Metadata;

namespace PhotoSelector.Tests;

public sealed class PhotoMetadataReaderTests
{
    [Fact]
    public void ReadCaptureTime_reads_jpeg_exif_date_time_original()
    {
        using var tempDirectory = new TempDirectory();
        var jpegPath = Path.Combine(tempDirectory.Path, "IMG_0001.JPG");
        File.WriteAllBytes(jpegPath, CreateExifJpeg("2026:06:18 10:11:12"));

        var captureTime = PhotoMetadataReader.ReadCaptureTime(jpegPath);

        Assert.Equal(new DateTimeOffset(2026, 6, 18, 10, 11, 12, TimeSpan.Zero), captureTime);
    }

    [Fact]
    public void ReadCaptureTime_returns_null_for_jpeg_without_exif_capture_time()
    {
        using var tempDirectory = new TempDirectory();
        var jpegPath = Path.Combine(tempDirectory.Path, "IMG_0001.JPG");
        File.WriteAllBytes(jpegPath, [0xFF, 0xD8, 0xFF, 0xD9]);

        Assert.Null(PhotoMetadataReader.ReadCaptureTime(jpegPath));
    }

    internal static byte[] CreateExifJpeg(string dateTimeOriginal)
    {
        var value = System.Text.Encoding.ASCII.GetBytes(dateTimeOriginal + '\0');
        var tiff = new List<byte>();
        tiff.AddRange([0x49, 0x49, 0x2A, 0x00]);
        tiff.AddRange(BitConverter.GetBytes(8u));
        tiff.AddRange(BitConverter.GetBytes((ushort)1));
        tiff.AddRange(BitConverter.GetBytes((ushort)0x8769));
        tiff.AddRange(BitConverter.GetBytes((ushort)4));
        tiff.AddRange(BitConverter.GetBytes(1u));
        tiff.AddRange(BitConverter.GetBytes(26u));
        tiff.AddRange(BitConverter.GetBytes(0u));
        tiff.AddRange(BitConverter.GetBytes((ushort)1));
        tiff.AddRange(BitConverter.GetBytes((ushort)0x9003));
        tiff.AddRange(BitConverter.GetBytes((ushort)2));
        tiff.AddRange(BitConverter.GetBytes((uint)value.Length));
        tiff.AddRange(BitConverter.GetBytes(44u));
        tiff.AddRange(BitConverter.GetBytes(0u));
        tiff.AddRange(value);

        var app1Payload = new List<byte>();
        app1Payload.AddRange(System.Text.Encoding.ASCII.GetBytes("Exif\0\0"));
        app1Payload.AddRange(tiff);
        var length = app1Payload.Count + 2;

        var jpeg = new List<byte> { 0xFF, 0xD8, 0xFF, 0xE1, (byte)(length >> 8), (byte)length };
        jpeg.AddRange(app1Payload);
        jpeg.AddRange([0xFF, 0xD9]);
        return jpeg.ToArray();
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
