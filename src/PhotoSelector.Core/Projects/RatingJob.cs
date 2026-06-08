namespace PhotoSelector.Core.Projects;

public sealed record RatingJob(
    long Id,
    long ProjectId,
    long PhotoId,
    string Status,
    int Attempts,
    string? LastError,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

