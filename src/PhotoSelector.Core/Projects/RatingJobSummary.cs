namespace PhotoSelector.Core.Projects;

public sealed record RatingJobSummary(
    int Total,
    int Pending,
    int Completed,
    int Failed);

