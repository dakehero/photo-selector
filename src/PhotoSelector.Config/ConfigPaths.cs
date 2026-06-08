namespace PhotoSelector.Config;

public static class ConfigPaths
{
    public const string ConfigHomeEnvironmentVariable = "PHOTO_SELECTOR_CONFIG_HOME";

    public static string GetConfigDirectory()
    {
        var overridePath = Environment.GetEnvironmentVariable(ConfigHomeEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return Path.GetFullPath(overridePath);
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            home = Environment.GetEnvironmentVariable("HOME")
                ?? Environment.GetEnvironmentVariable("USERPROFILE")
                ?? Environment.CurrentDirectory;
        }

        return Path.Combine(home, ".photo-selector");
    }

    public static string GetConfigPath()
    {
        return Path.Combine(GetConfigDirectory(), "config.toml");
    }

    public static string GetDatabasePath()
    {
        return Path.Combine(GetConfigDirectory(), "photo-selector.db");
    }
}
