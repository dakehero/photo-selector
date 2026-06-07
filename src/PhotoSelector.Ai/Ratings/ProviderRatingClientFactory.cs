namespace PhotoSelector.Ai.Ratings;

public static class ProviderRatingClientFactory
{
    public static IPhotoRatingClient Create(string provider)
    {
        var normalized = Normalize(provider);
        return normalized switch
        {
            "openai" => new OpenAiSdkRatingClient(),
            "openrouter" or "openai-compatible" or "lmstudio" or "ollama" or "litellm" or "vllm" or "localai" =>
                new OpenAiCompatibleRatingClient(),
            _ => throw new NotSupportedException($"Unsupported AI provider: {provider}"),
        };
    }

    public static bool IsSupported(string provider)
    {
        return Normalize(provider) is
            "openai" or
            "openrouter" or
            "openai-compatible" or
            "lmstudio" or
            "ollama" or
            "litellm" or
            "vllm" or
            "localai";
    }

    private static string Normalize(string provider)
    {
        return provider.Trim().ToLowerInvariant();
    }
}
