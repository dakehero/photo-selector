namespace PhotoSelector.Core.Projects;

public sealed record PhotoItem(
    long Id,
    long ProjectId,
    string BaseName,
    string? JpegPath,
    string? RawPath,
    DateTimeOffset? CaptureTime,
    string ImportStatus);
