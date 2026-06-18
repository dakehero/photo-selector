namespace PhotoSelector.Core.Grouping;

public sealed record SequenceGroupingOptions(
    int MaxFilenameGap = 2,
    TimeSpan? MaxCaptureTimeGap = null)
{
    public static SequenceGroupingOptions Default { get; } = new(
        MaxFilenameGap: 2,
        MaxCaptureTimeGap: TimeSpan.FromSeconds(10));

    public IReadOnlyList<GroupingPipelineStage> Stages { get; } =
    [
        new("filename-sequence", "applied"),
        new("capture-time-window", MaxCaptureTimeGap is null ? "disabled" : "applied-when-present"),
        new("ai-encoder", "reserved"),
    ];
}
