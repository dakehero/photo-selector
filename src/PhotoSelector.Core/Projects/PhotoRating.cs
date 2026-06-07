namespace PhotoSelector.Core.Projects;

public sealed record PhotoRating(
    long Id,
    long PhotoId,
    string Provider,
    string Model,
    string PhotoType,
    double Score,
    string Category,
    string CriteriaJson,
    string Reason,
    DateTimeOffset CreatedAt);
