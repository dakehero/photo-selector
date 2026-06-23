namespace PhotoSelector.Core.Projects;

public static class PhotoImportStatus
{
    public const string Imported = "imported";

    public const string Changed = "changed";

    public const string Missing = "missing";

    public static bool IsMissing(string status)
    {
        return string.Equals(status, Missing, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsImported(string status)
    {
        return string.Equals(status, Imported, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsChanged(string status)
    {
        return string.Equals(status, Changed, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsCurrent(string status)
    {
        return !IsMissing(status);
    }
}
