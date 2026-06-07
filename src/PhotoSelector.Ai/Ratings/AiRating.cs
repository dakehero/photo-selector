namespace PhotoSelector.Ai.Ratings;

public sealed record AiRatingCriterion(string Name, double Score, string Comment);

public sealed record AiRating(
    string PhotoType,
    double Score,
    string Category,
    IReadOnlyList<AiRatingCriterion> Criteria,
    string Reason);

public sealed record AiRatingAudit(
    string Prompt,
    string RequestJsonRedacted,
    string RawMessageContent,
    string RawResponseJson,
    int? HttpStatus,
    string? Error);

public sealed record AiRatingClientResult(AiRating? Rating, AiRatingAudit Audit);
