using System.Text.Json;

namespace PhotoSelector.Ai.Ratings;

internal static class ChatCompletionResponseParser
{
    public static string ExtractMessageContent(string responseBody)
    {
        var response = JsonSerializer.Deserialize(responseBody, RatingJsonContext.Default.ChatCompletionsResponseJson);
        var firstChoice = response?.Choices is { Length: > 0 } choices ? choices[0] : null;
        if (firstChoice?.Message is null)
        {
            throw new InvalidOperationException("AI response did not include choices.");
        }

        return firstChoice.Message.Content.Text ?? string.Empty;
    }

    public static string ExtractJsonObject(string content)
    {
        var trimmed = content.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = trimmed.IndexOf("\n", StringComparison.Ordinal);
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewLine >= 0 && lastFence > firstNewLine)
            {
                trimmed = trimmed[(firstNewLine + 1)..lastFence].Trim();
            }
        }

        if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
        {
            return trimmed;
        }

        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            return trimmed[start..(end + 1)];
        }

        return trimmed;
    }
}
