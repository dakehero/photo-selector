namespace PhotoSelector.Core.Projects;

public sealed record ArenaRating(
    long Id,
    long ArenaRunId,
    long PhotoId,
    string Provider,
    string Model,
    string? PhotoType,
    double? Score,
    string? Category,
    string CriteriaJson,
    string Reason,
    string Prompt,
    string RequestJsonRedacted,
    string RawMessageContent,
    string RawResponseJson,
    int? HttpStatus,
    string? Error,
    DateTimeOffset CreatedAt);
