namespace PhotoSelector.Core.Projects;

public sealed record PhotoItem(
    long Id,
    long ProjectId,
    string BaseName,
    string? JpegPath,
    string? RawPath,
    DateTimeOffset? CaptureTime,
    string ImportStatus,
    long? JpegSize = null,
    DateTimeOffset? JpegModifiedAt = null,
    long? RawSize = null,
    DateTimeOffset? RawModifiedAt = null);
