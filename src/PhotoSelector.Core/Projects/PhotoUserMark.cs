namespace PhotoSelector.Core.Projects;

public sealed record PhotoUserMark(
    long Id,
    long PhotoId,
    string Decision,
    int Stars,
    string Note,
    DateTimeOffset UpdatedAt);
