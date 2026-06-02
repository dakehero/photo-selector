using PhotoSelector.Core.Files;
using PhotoSelector.Core.Scanning;

namespace PhotoSelector.Tests;

public sealed class PhotoScannerTests
{
    [Theory]
    [InlineData("IMG_0001.JPG", PhotoFileKind.Jpeg)]
    [InlineData("IMG_0001.jpeg", PhotoFileKind.Jpeg)]
    [InlineData("IMG_0001.CR3", PhotoFileKind.Raw)]
    [InlineData("IMG_0001.nef", PhotoFileKind.Raw)]
    [InlineData("notes.txt", PhotoFileKind.Unsupported)]
    public void Classify_detects_supported_extensions_case_insensitively(
        string path,
        PhotoFileKind expected)
    {
        Assert.Equal(expected, PhotoFileClassifier.Classify(path));
    }

    [Fact]
    public void ScanFiles_pairs_jpeg_and_raw_files_with_the_same_stem()
    {
        var files = new[]
        {
            Path.Combine("shoot", "IMG_0001.JPG"),
            Path.Combine("shoot", "IMG_0001.CR3"),
            Path.Combine("shoot", "IMG_0002.JPG"),
        };

        var pairs = PhotoScanner.ScanFiles(files);

        Assert.Collection(
            pairs,
            pair =>
            {
                Assert.Equal("IMG_0001", pair.BaseName);
                Assert.Equal(Path.Combine("shoot", "IMG_0001.JPG"), pair.JpegPath);
                Assert.Equal(Path.Combine("shoot", "IMG_0001.CR3"), pair.RawPath);
            },
            pair =>
            {
                Assert.Equal("IMG_0002", pair.BaseName);
                Assert.Equal(Path.Combine("shoot", "IMG_0002.JPG"), pair.JpegPath);
                Assert.Null(pair.RawPath);
            });
    }

    [Fact]
    public void ScanFiles_pairs_jpeg_and_raw_files_with_differently_cased_stems()
    {
        var files = new[]
        {
            Path.Combine("shoot", "IMG_0001.JPG"),
            Path.Combine("shoot", "img_0001.CR3"),
        };

        var pair = Assert.Single(PhotoScanner.ScanFiles(files));

        Assert.Equal("img_0001", pair.BaseName);
        Assert.Equal(Path.Combine("shoot", "IMG_0001.JPG"), pair.JpegPath);
        Assert.Equal(Path.Combine("shoot", "img_0001.CR3"), pair.RawPath);
    }

    [Fact]
    public void ScanFiles_uses_first_sorted_path_for_duplicate_same_kind_files()
    {
        var files = new[]
        {
            Path.Combine("shoot", "IMG_0001.jpeg"),
            Path.Combine("shoot", "IMG_0001.JPG"),
            Path.Combine("shoot", "IMG_0001.ARW"),
            Path.Combine("shoot", "IMG_0001.CR3"),
        };

        var pair = Assert.Single(PhotoScanner.ScanFiles(files));

        Assert.Equal("IMG_0001", pair.BaseName);
        Assert.Equal(Path.Combine("shoot", "IMG_0001.jpeg"), pair.JpegPath);
        Assert.Equal(Path.Combine("shoot", "IMG_0001.ARW"), pair.RawPath);
    }
}
