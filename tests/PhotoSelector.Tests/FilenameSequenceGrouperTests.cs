using PhotoSelector.Core.Grouping;
using PhotoSelector.Core.Projects;

namespace PhotoSelector.Tests;

public sealed class FilenameSequenceGrouperTests
{
    [Fact]
    public void Group_returns_sequence_for_shared_prefix_and_numbers_within_gap()
    {
        var photos = new[]
        {
            Photo(1, "IMG_0001"),
            Photo(2, "IMG_0002"),
            Photo(3, "IMG_0004"),
            Photo(4, "DSC_0100"),
            Photo(5, "DSC_0101"),
        };

        var groups = FilenameSequenceGrouper.Group(photos, maxGap: 2);

        Assert.Collection(
            groups,
            group =>
            {
                Assert.Equal("filename-sequence:DSC_:0100-0101", group.Id);
                Assert.Equal("sequence", group.Type);
                Assert.Equal("DSC_", group.Key);
                Assert.Equal("filename sequence within gap 2", group.Reason);
                Assert.Equal(["DSC_0100", "DSC_0101"], group.Items.Select(item => item.BaseName).ToArray());
                Assert.Equal([100, 101], group.Items.Select(item => item.SequenceNumber).ToArray());
            },
            group =>
            {
                Assert.Equal("filename-sequence:IMG_:0001-0004", group.Id);
                Assert.Equal("IMG_", group.Key);
                Assert.Equal(["IMG_0001", "IMG_0002", "IMG_0004"], group.Items.Select(item => item.BaseName).ToArray());
                Assert.Equal([0, 1, 2], group.Items.Select(item => item.Order).ToArray());
            });
    }

    [Fact]
    public void Group_splits_sequences_when_numeric_gap_is_too_large()
    {
        var photos = new[]
        {
            Photo(1, "IMG_0001"),
            Photo(2, "IMG_0002"),
            Photo(3, "IMG_0010"),
            Photo(4, "IMG_0011"),
        };

        var groups = FilenameSequenceGrouper.Group(photos, maxGap: 2);

        Assert.Collection(
            groups,
            group => Assert.Equal(["IMG_0001", "IMG_0002"], group.Items.Select(item => item.BaseName).ToArray()),
            group => Assert.Equal(["IMG_0010", "IMG_0011"], group.Items.Select(item => item.BaseName).ToArray()));
    }

    [Fact]
    public void Group_splits_sequences_when_capture_time_gap_is_too_large()
    {
        var start = DateTimeOffset.Parse("2026-06-18T10:00:00Z");
        var photos = new[]
        {
            Photo(1, "IMG_0001", start),
            Photo(2, "IMG_0002", start.AddSeconds(2)),
            Photo(3, "IMG_0003", start.AddMinutes(5)),
            Photo(4, "IMG_0004", start.AddMinutes(5).AddSeconds(2)),
        };
        var options = new SequenceGroupingOptions(
            MaxFilenameGap: 2,
            MaxCaptureTimeGap: TimeSpan.FromSeconds(10));

        var groups = FilenameSequenceGrouper.Group(photos, options);

        Assert.Collection(
            groups,
            group =>
            {
                Assert.Equal("filename-sequence:IMG_:0001-0002", group.Id);
                Assert.Equal(["IMG_0001", "IMG_0002"], group.Items.Select(item => item.BaseName).ToArray());
            },
            group =>
            {
                Assert.Equal("filename-sequence:IMG_:0003-0004", group.Id);
                Assert.Equal(["IMG_0003", "IMG_0004"], group.Items.Select(item => item.BaseName).ToArray());
            });
    }

    [Fact]
    public void Group_does_not_split_on_capture_time_when_metadata_is_missing()
    {
        var start = DateTimeOffset.Parse("2026-06-18T10:00:00Z");
        var photos = new[]
        {
            Photo(1, "IMG_0001", start),
            Photo(2, "IMG_0002"),
            Photo(3, "IMG_0003", start.AddMinutes(5)),
        };

        var group = Assert.Single(FilenameSequenceGrouper.Group(
            photos,
            new SequenceGroupingOptions(
                MaxFilenameGap: 2,
                MaxCaptureTimeGap: TimeSpan.FromSeconds(10))));

        Assert.Equal(["IMG_0001", "IMG_0002", "IMG_0003"], group.Items.Select(item => item.BaseName).ToArray());
    }

    [Fact]
    public void Group_omits_singletons_and_names_without_trailing_numbers()
    {
        var photos = new[]
        {
            Photo(1, "cover"),
            Photo(2, "IMG_A"),
            Photo(3, "IMG_0001"),
            Photo(4, "DSC_0100"),
            Photo(5, "DSC_0101"),
        };

        var group = Assert.Single(FilenameSequenceGrouper.Group(photos, maxGap: 2));

        Assert.Equal("DSC_", group.Key);
        Assert.Equal(["DSC_0100", "DSC_0101"], group.Items.Select(item => item.BaseName).ToArray());
    }

    private static PhotoItem Photo(long id, string baseName, DateTimeOffset? captureTime = null)
    {
        return new PhotoItem(
            id,
            10,
            baseName,
            Path.Combine("shoot", baseName + ".JPG"),
            null,
            captureTime,
            "imported");
    }
}
