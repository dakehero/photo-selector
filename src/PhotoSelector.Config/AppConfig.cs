namespace PhotoSelector.Config;

public sealed class AppConfig
{
    public string ActiveProfile { get; set; } = "default";

    public Dictionary<string, AiProfile> Profiles { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["default"] = new AiProfile(),
    };

    public AiProfile GetOrCreateProfile(string profileName)
    {
        if (!Profiles.TryGetValue(profileName, out var profile))
        {
            profile = new AiProfile();
            Profiles[profileName] = profile;
        }

        return profile;
    }
}

public sealed class AiProfile
{
    public string Provider { get; set; } = "openai-compatible";

    public string BaseUrl { get; set; } = "https://api.openai.com/v1";

    public string Model { get; set; } = "gpt-4.1-mini";

    public string? ApiKeyRef { get; set; }

    public string? ApiKeyEnv { get; set; }

    public int Concurrency { get; set; } = 2;
}
