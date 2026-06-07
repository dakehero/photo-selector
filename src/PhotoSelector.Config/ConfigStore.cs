using System.Globalization;
using System.Text;

namespace PhotoSelector.Config;

public sealed class ConfigStore
{
    private readonly string configPath;

    public ConfigStore(string? configPath = null)
    {
        this.configPath = configPath ?? ConfigPaths.GetConfigPath();
    }

    public string ConfigPath => configPath;

    public AppConfig Load()
    {
        if (!File.Exists(configPath))
        {
            return new AppConfig();
        }

        var config = new AppConfig();
        AiProfile? currentProfile = null;

        foreach (var rawLine in File.ReadAllLines(configPath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("[profiles.", StringComparison.Ordinal) && line.EndsWith(']'))
            {
                var profileName = line["[profiles.".Length..^1];
                currentProfile = config.GetOrCreateProfile(profileName);
                continue;
            }

            var separator = line.IndexOf('=', StringComparison.Ordinal);
            if (separator < 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();

            if (currentProfile is null)
            {
                if (key == "active_profile")
                {
                    config.ActiveProfile = ParseString(value) ?? config.ActiveProfile;
                }

                continue;
            }

            ApplyProfileValue(currentProfile, key, value);
        }

        config.GetOrCreateProfile(config.ActiveProfile);
        return config;
    }

    public void Save(AppConfig config)
    {
        var directory = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(configPath, Serialize(config), Encoding.UTF8);
    }

    private static string Serialize(AppConfig config)
    {
        var builder = new StringBuilder();
        builder.Append("active_profile = ");
        AppendTomlString(builder, config.ActiveProfile);
        builder.AppendLine();
        builder.AppendLine();

        foreach (var profile in config.Profiles.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append("[profiles.");
            builder.Append(profile.Key);
            builder.AppendLine("]");
            AppendString(builder, "provider", profile.Value.Provider);
            AppendString(builder, "base_url", profile.Value.BaseUrl);
            AppendString(builder, "model", profile.Value.Model);
            AppendNullableString(builder, "api_key_ref", profile.Value.ApiKeyRef);
            AppendNullableString(builder, "api_key_env", profile.Value.ApiKeyEnv);
            AppendNullableString(builder, "prompt", profile.Value.Prompt);
            AppendString(builder, "output_language", profile.Value.OutputLanguage);
            builder.Append("concurrency = ");
            builder.AppendLine(profile.Value.Concurrency.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static void ApplyProfileValue(AiProfile profile, string key, string value)
    {
        switch (key)
        {
            case "provider":
                profile.Provider = ParseString(value) ?? profile.Provider;
                break;
            case "base_url":
                profile.BaseUrl = ParseString(value) ?? profile.BaseUrl;
                break;
            case "model":
                profile.Model = ParseString(value) ?? profile.Model;
                break;
            case "api_key_ref":
                profile.ApiKeyRef = ParseString(value);
                break;
            case "api_key_env":
                profile.ApiKeyEnv = ParseString(value);
                break;
            case "prompt":
                profile.Prompt = ParseString(value);
                break;
            case "output_language":
                profile.OutputLanguage = ParseString(value) ?? profile.OutputLanguage;
                break;
            case "concurrency":
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var concurrency))
                {
                    profile.Concurrency = Math.Max(1, concurrency);
                }

                break;
        }
    }

    private static void AppendString(StringBuilder builder, string key, string value)
    {
        builder.Append(key);
        builder.Append(" = ");
        AppendTomlString(builder, value);
        builder.AppendLine();
    }

    private static void AppendNullableString(StringBuilder builder, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        AppendString(builder, key, value);
    }

    private static void AppendTomlString(StringBuilder builder, string value)
    {
        builder.Append('"');
        builder.Append(value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal));
        builder.Append('"');
    }

    private static string? ParseString(string value)
    {
        if (value.Length < 2 || value[0] != '"' || value[^1] != '"')
        {
            return null;
        }

        return value[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
    }
}
