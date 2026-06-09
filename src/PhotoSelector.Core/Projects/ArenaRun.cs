namespace PhotoSelector.Core.Projects;

public sealed record ArenaRun(
    long Id,
    long ProjectId,
    string Provider,
    string ModelsCsv,
    string Prompt,
    string OutputLanguage,
    int Limit,
    DateTimeOffset CreatedAt);
