namespace PhotoSelector.Core.Grouping;

public sealed record PhotoGroupItem(
    long PhotoId,
    string BaseName,
    int Order,
    long SequenceNumber);
