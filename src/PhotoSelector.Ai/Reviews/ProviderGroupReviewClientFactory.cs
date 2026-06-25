namespace PhotoSelector.Ai.Reviews;

public static class ProviderGroupReviewClientFactory
{
    public static IGroupReviewClient Create(string provider)
    {
        return IsSupported(provider)
            ? new OpenAiCompatibleGroupReviewClient()
            : throw new NotSupportedException($"Unsupported AI provider: {provider}");
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
