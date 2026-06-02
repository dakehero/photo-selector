namespace PhotoSelector.Core.Exporting;

public sealed record ExportResult(
    string ExportDirectory,
    IReadOnlyList<string> ExportedFiles);
