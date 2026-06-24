namespace PhotoSelector.Core.Projects;

public sealed record GroupReview(
    long Id,
    long ProjectId,
    string GroupId,
    string GroupType,
    string GroupKey,
    string GroupReason,
    long WinnerPhotoId,
    string WinnerBaseName,
    string Reason,
    string Provider,
    string Model,
    string Prompt,
    DateTimeOffset CreatedAt);

public sealed record GroupReviewItem(
    long Id,
    long GroupReviewId,
    long PhotoId,
    string BaseName,
    string? JpegPath,
    string? RawPath,
    DateTimeOffset? CaptureTime,
    string ImportStatus,
    long? JpegSize,
    DateTimeOffset? JpegModifiedAt,
    long? RawSize,
    DateTimeOffset? RawModifiedAt,
    int Order,
    long SequenceNumber);

public sealed record GroupReviewItemSnapshot(
    long PhotoId,
    string BaseName,
    string? JpegPath,
    string? RawPath,
    DateTimeOffset? CaptureTime,
    string ImportStatus,
    long? JpegSize,
    DateTimeOffset? JpegModifiedAt,
    long? RawSize,
    DateTimeOffset? RawModifiedAt,
    int Order,
    long SequenceNumber);
