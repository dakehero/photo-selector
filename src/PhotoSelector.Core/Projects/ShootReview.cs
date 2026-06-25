namespace PhotoSelector.Core.Projects;

public sealed record ShootReview(
    long Id,
    long ProjectId,
    string SummaryText,
    string SummaryJson,
    string TopCandidatesJson,
    string GroupReviewsJson,
    string WeakPatternsJson,
    string NextShootNotesJson,
    DateTimeOffset CreatedAt);
