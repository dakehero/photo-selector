using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace PhotoSelector.Core.Metadata;

public static class PhotoMetadataReader
{
    private const ushort ExifIfdPointerTag = 0x8769;
    private const ushort DateTimeOriginalTag = 0x9003;

    public static DateTimeOffset? ReadCaptureTime(string? jpegPath)
    {
        if (string.IsNullOrWhiteSpace(jpegPath) || !File.Exists(jpegPath))
        {
            return null;
        }

        try
        {
            var bytes = File.ReadAllBytes(jpegPath);
            return ReadJpegExifCaptureTime(bytes);
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

    private static DateTimeOffset? ReadJpegExifCaptureTime(ReadOnlySpan<byte> jpeg)
    {
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
        {
            return null;
        }

        var offset = 2;
        while (offset + 4 <= jpeg.Length)
        {
            if (jpeg[offset] != 0xFF)
            {
                return null;
            }

            var marker = jpeg[offset + 1];
            offset += 2;
            if (marker == 0xD9 || marker == 0xDA)
            {
                return null;
            }

            if (offset + 2 > jpeg.Length)
            {
                return null;
            }

            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(jpeg.Slice(offset, 2));
            if (segmentLength < 2 || offset + segmentLength > jpeg.Length)
            {
                return null;
            }

            var payload = jpeg.Slice(offset + 2, segmentLength - 2);
            if (marker == 0xE1 && payload.StartsWith("Exif\0\0"u8))
            {
                return ReadTiffCaptureTime(payload[6..]);
            }

            offset += segmentLength;
        }

        return null;
    }

    private static DateTimeOffset? ReadTiffCaptureTime(ReadOnlySpan<byte> tiff)
    {
        if (tiff.Length < 8)
        {
            return null;
        }

        var littleEndian = tiff[0] == 0x49 && tiff[1] == 0x49;
        var bigEndian = tiff[0] == 0x4D && tiff[1] == 0x4D;
        if (!littleEndian && !bigEndian)
        {
            return null;
        }

        if (ReadUInt16(tiff[2..4], littleEndian) != 42)
        {
            return null;
        }

        var ifd0Offset = ReadUInt32(tiff[4..8], littleEndian);
        var exifIfdOffset = ReadIfdValueOffset(tiff, ifd0Offset, ExifIfdPointerTag, littleEndian);
        if (exifIfdOffset is null)
        {
            return null;
        }

        var dateTimeOffset = ReadIfdValueOffset(tiff, exifIfdOffset.Value, DateTimeOriginalTag, littleEndian);
        return dateTimeOffset is null ? null : ReadAsciiDateTime(tiff, dateTimeOffset.Value);
    }

    private static uint? ReadIfdValueOffset(ReadOnlySpan<byte> tiff, uint ifdOffset, ushort tag, bool littleEndian)
    {
        if (ifdOffset > tiff.Length - 2)
        {
            return null;
        }

        var count = ReadUInt16(tiff.Slice((int)ifdOffset, 2), littleEndian);
        var entryOffset = (int)ifdOffset + 2;
        for (var index = 0; index < count; index++)
        {
            if (entryOffset + 12 > tiff.Length)
            {
                return null;
            }

            var entry = tiff.Slice(entryOffset, 12);
            if (ReadUInt16(entry[0..2], littleEndian) == tag)
            {
                return ReadUInt32(entry[8..12], littleEndian);
            }

            entryOffset += 12;
        }

        return null;
    }

    private static DateTimeOffset? ReadAsciiDateTime(ReadOnlySpan<byte> tiff, uint valueOffset)
    {
        if (valueOffset >= tiff.Length)
        {
            return null;
        }

        var bytes = tiff[(int)valueOffset..];
        var terminator = bytes.IndexOf((byte)0);
        if (terminator < 0)
        {
            return null;
        }

        var text = Encoding.ASCII.GetString(bytes[..terminator]);
        return DateTime.TryParseExact(
            text,
            "yyyy:MM:dd HH:mm:ss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var dateTime)
            ? new DateTimeOffset(DateTime.SpecifyKind(dateTime, DateTimeKind.Unspecified), TimeSpan.Zero)
            : null;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, bool littleEndian)
    {
        return littleEndian
            ? BinaryPrimitives.ReadUInt16LittleEndian(bytes)
            : BinaryPrimitives.ReadUInt16BigEndian(bytes);
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, bool littleEndian)
    {
        return littleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(bytes)
            : BinaryPrimitives.ReadUInt32BigEndian(bytes);
    }
}
