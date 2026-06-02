namespace PhotoSelector.Core.Projects;

public sealed record PhotoProject(
    long Id,
    string SourceDirectory,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastOpenedAt);
