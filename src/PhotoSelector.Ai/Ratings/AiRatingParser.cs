using System.Text.Json;

namespace PhotoSelector.Ai.Ratings;

public sealed record AiRatingParseResult(bool IsSuccess, AiRating? Rating, string? Error);

public static class AiRatingParser
{
    private static readonly HashSet<string> ValidCategories = new(StringComparer.Ordinal)
    {
        "keep",
        "maybe",
        "reject",
    };

    public static AiRatingParseResult Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return Failure("AI rating JSON root must be an object.");
            }

            if (!root.TryGetProperty("score", out var scoreElement) ||
                !root.TryGetProperty("category", out var categoryElement) ||
                !root.TryGetProperty("reason", out var reasonElement))
            {
                return Failure("AI rating JSON must include score, category, and reason.");
            }

            if (scoreElement.ValueKind != JsonValueKind.Number)
            {
                return Failure("Score must be a number.");
            }

            if (!scoreElement.TryGetInt32(out var score))
            {
                return Failure("Score must be an integer.");
            }

            if (score is < 1 or > 5)
            {
                return Failure("Score must be between 1 and 5.");
            }

            if (categoryElement.ValueKind != JsonValueKind.String)
            {
                return Failure("Category must be a string.");
            }

            var category = categoryElement.GetString();
            if (category is null || !ValidCategories.Contains(category))
            {
                return Failure("Category must be one of keep, maybe, or reject.");
            }

            if (reasonElement.ValueKind != JsonValueKind.String)
            {
                return Failure("Reason must be a string.");
            }

            return new AiRatingParseResult(
                true,
                new AiRating(score, category, reasonElement.GetString() ?? string.Empty),
                null);
        }
        catch (JsonException)
        {
            return Failure("AI rating JSON is malformed.");
        }
        catch (ArgumentException)
        {
            return Failure("AI rating JSON is malformed.");
        }
    }

    private static AiRatingParseResult Failure(string error) => new(false, null, error);
}
