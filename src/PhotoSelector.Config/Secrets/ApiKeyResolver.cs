namespace PhotoSelector.Config.Secrets;

public sealed record ApiKeyResolution(bool IsAvailable, string Source, string? Error, string? Secret = null);

public static class ApiKeyResolver
{
    public static ApiKeyResolution Resolve(AiProfile profile, ISecretStore secretStore)
    {
        string? apiKeyRefError = null;
        if (!string.IsNullOrWhiteSpace(profile.ApiKeyRef))
        {
            try
            {
                var secret = secretStore.Get(profile.ApiKeyRef);
                if (!string.IsNullOrEmpty(secret))
                {
                    return new ApiKeyResolution(true, "api_key_ref", null, secret);
                }
            }
            catch (Exception ex)
            {
                apiKeyRefError = $"Secret {profile.ApiKeyRef} is not available: {ex.Message}";
            }
        }

        if (!string.IsNullOrWhiteSpace(profile.ApiKeyEnv))
        {
            var secret = Environment.GetEnvironmentVariable(profile.ApiKeyEnv);
            if (!string.IsNullOrEmpty(secret))
            {
                return new ApiKeyResolution(true, "api_key_env", null, secret);
            }

            return new ApiKeyResolution(false, "api_key_env", $"Environment variable {profile.ApiKeyEnv} is not set.");
        }

        if (!string.IsNullOrWhiteSpace(profile.ApiKeyRef))
        {
            return new ApiKeyResolution(false, "api_key_ref", apiKeyRefError ?? $"Secret {profile.ApiKeyRef} is not available.");
        }

        return new ApiKeyResolution(false, "none", "No api_key_ref or api_key_env is configured.");
    }
}
