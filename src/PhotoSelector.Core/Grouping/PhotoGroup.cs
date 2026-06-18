namespace PhotoSelector.Core.Grouping;

public sealed record PhotoGroup(
    string Id,
    string Type,
    string Key,
    string Reason,
    IReadOnlyList<PhotoGroupItem> Items);
