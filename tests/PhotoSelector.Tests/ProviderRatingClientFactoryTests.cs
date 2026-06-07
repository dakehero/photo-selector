using PhotoSelector.Ai.Ratings;

namespace PhotoSelector.Tests;

public sealed class ProviderRatingClientFactoryTests
{
    [Theory]
    [InlineData("openai", typeof(OpenAiSdkRatingClient))]
    [InlineData("openrouter", typeof(OpenAiCompatibleRatingClient))]
    [InlineData("openai-compatible", typeof(OpenAiCompatibleRatingClient))]
    [InlineData("lmstudio", typeof(OpenAiCompatibleRatingClient))]
    [InlineData("ollama", typeof(OpenAiCompatibleRatingClient))]
    public void Create_returns_expected_client_for_supported_provider(string provider, Type expectedType)
    {
        using var client = ProviderRatingClientFactory.Create(provider);

        Assert.IsType(expectedType, client);
    }

    [Fact]
    public void Create_rejects_unknown_provider()
    {
        var error = Assert.Throws<NotSupportedException>(() => ProviderRatingClientFactory.Create("unknown-ai"));

        Assert.Contains("unknown-ai", error.Message);
    }
}
