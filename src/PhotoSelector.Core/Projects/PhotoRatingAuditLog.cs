namespace PhotoSelector.Core.Projects;

public sealed record PhotoRatingAuditLog(
    long Id,
    long PhotoId,
    long? RatingId,
    string Provider,
    string Model,
    string Prompt,
    string RequestJsonRedacted,
    string RawMessageContent,
    string RawResponseJson,
    int? HttpStatus,
    string? Error,
    DateTimeOffset CreatedAt);
