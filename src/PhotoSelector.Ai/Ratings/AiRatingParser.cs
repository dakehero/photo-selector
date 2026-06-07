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

            if (!root.TryGetProperty("photo_type", out var photoTypeElement) ||
                !root.TryGetProperty("score", out var scoreElement) ||
                !root.TryGetProperty("category", out var categoryElement) ||
                !root.TryGetProperty("criteria", out var criteriaElement) ||
                !root.TryGetProperty("reason", out var reasonElement))
            {
                return Failure("AI rating JSON must include photo_type, score, category, criteria, and reason.");
            }

            if (photoTypeElement.ValueKind != JsonValueKind.String)
            {
                return Failure("Photo type must be a string.");
            }

            var photoType = photoTypeElement.GetString();
            if (string.IsNullOrWhiteSpace(photoType))
            {
                return Failure("Photo type must not be empty.");
            }

            if (scoreElement.ValueKind != JsonValueKind.Number)
            {
                return Failure("Score must be a number.");
            }

            if (!TryGetOneDecimalScore(scoreElement, out var score))
            {
                return Failure("Score must be a number with at most one decimal place.");
            }

            if (score is < 1 or > 10)
            {
                return Failure("Score must be between 1 and 10.");
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

            if (!IsCategoryConsistentWithScore(score, category))
            {
                return Failure("Category must match the score: keep 8-10, maybe 5-7, reject 1-4.");
            }

            if (reasonElement.ValueKind != JsonValueKind.String)
            {
                return Failure("Reason must be a string.");
            }

            if (criteriaElement.ValueKind != JsonValueKind.Array)
            {
                return Failure("Criteria must be an array.");
            }

            var criteria = new List<AiRatingCriterion>();
            foreach (var item in criteriaElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    return Failure("Each criterion must be an object.");
                }

                if (!item.TryGetProperty("name", out var nameElement) ||
                    !item.TryGetProperty("score", out var criterionScoreElement) ||
                    !item.TryGetProperty("comment", out var commentElement))
                {
                    return Failure("Each criterion must include name, score, and comment.");
                }

                if (nameElement.ValueKind != JsonValueKind.String ||
                    criterionScoreElement.ValueKind != JsonValueKind.Number ||
                    commentElement.ValueKind != JsonValueKind.String)
                {
                    return Failure("Criterion name/comment must be strings and score must be a number.");
                }

            if (!TryGetOneDecimalScore(criterionScoreElement, out var criterionScore))
            {
                return Failure("Criterion score must be a number with at most one decimal place.");
            }

                if (criterionScore is < 1 or > 10)
                {
                    return Failure("Criterion score must be between 1 and 10.");
                }

                var name = nameElement.GetString();
                if (string.IsNullOrWhiteSpace(name))
                {
                    return Failure("Criterion name must not be empty.");
                }

                criteria.Add(new AiRatingCriterion(name, criterionScore, commentElement.GetString() ?? string.Empty));
            }

            return new AiRatingParseResult(
                true,
                new AiRating(photoType, score, category, criteria, reasonElement.GetString() ?? string.Empty),
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

    private static bool IsCategoryConsistentWithScore(double score, string category)
    {
        return category switch
        {
            "keep" => score is >= 8.0 and <= 10.0,
            "maybe" => score is >= 5.0 and < 8.0,
            "reject" => score is >= 1.0 and < 5.0,
            _ => false,
        };
    }

    private static bool TryGetOneDecimalScore(JsonElement element, out double score)
    {
        score = 0;
        var rawText = element.GetRawText();
        var decimalSeparator = rawText.IndexOf('.', StringComparison.Ordinal);
        if (decimalSeparator < 0 || rawText.Length - decimalSeparator - 1 != 1)
        {
            return false;
        }

        if (!element.TryGetDouble(out var value))
        {
            return false;
        }

        var scaled = value * 10;
        if (Math.Abs(scaled - Math.Round(scaled)) > 0.000001)
        {
            return false;
        }

        score = Math.Round(value, 1);
        return true;
    }
}
