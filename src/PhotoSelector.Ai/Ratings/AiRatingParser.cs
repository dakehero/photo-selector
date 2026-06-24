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
        AiRatingResponseJson? response;
        try
        {
            response = JsonSerializer.Deserialize(json, RatingJsonContext.Default.AiRatingResponseJson);
        }
        catch (JsonException)
        {
            return Failure("AI rating JSON is malformed.");
        }
        catch (ArgumentException)
        {
            return Failure("AI rating JSON is malformed.");
        }

        if (response is null)
        {
            return Failure("AI rating JSON root must be an object.");
        }

        if (response is not
            {
                PhotoType: { } photoType,
                Score: { } scoreDto,
                Category: { } category,
                Criteria: { } criteriaItems,
                Reason: { } reason,
            })
        {
            return Failure("AI rating JSON must include photo_type, score, category, criteria, and reason.");
        }

        if (string.IsNullOrWhiteSpace(photoType))
        {
            return Failure("Photo type must not be empty.");
        }

        var scoreResult = ValidateScore(scoreDto, "Score");
        if (!scoreResult.IsSuccess)
        {
            return Failure(scoreResult.Error!);
        }

        var score = scoreResult.Score;
        if (!ValidCategories.Contains(category))
        {
            return Failure("Category must be one of keep, maybe, or reject.");
        }

        if (!IsCategoryConsistentWithScore(score, category))
        {
            return Failure("Category must match the score: keep 8-10, maybe 5-7, reject 1-4.");
        }

        var criteria = new List<AiRatingCriterion>();
        foreach (var item in criteriaItems)
        {
            if (item.Name is null || item.Score is null || item.Comment is null)
            {
                return Failure("Each criterion must include name, score, and comment.");
            }

            if (string.IsNullOrWhiteSpace(item.Name))
            {
                return Failure("Criterion name must not be empty.");
            }

            var criterionScoreResult = ValidateScore(item.Score.Value, "Criterion score");
            if (!criterionScoreResult.IsSuccess)
            {
                return Failure(criterionScoreResult.Error!);
            }

            criteria.Add(new AiRatingCriterion(item.Name, criterionScoreResult.Score, item.Comment));
        }

        return new AiRatingParseResult(
            true,
            new AiRating(photoType, score, category, criteria, reason),
            null);
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

    private static ScoreValidationResult ValidateScore(RatingScore score, string label)
    {
        if (!HasExactlyOneDecimalPlace(score.RawText))
        {
            return new ScoreValidationResult(false, 0, $"{label} must be a number with exactly one decimal place.");
        }

        if (score.Value is < 1 or > 10)
        {
            return new ScoreValidationResult(false, 0, $"{label} must be between 1 and 10.");
        }

        var scaled = score.Value * 10;
        if (Math.Abs(scaled - Math.Round(scaled)) > 0.000001)
        {
            return new ScoreValidationResult(false, 0, $"{label} must be a number with exactly one decimal place.");
        }

        return new ScoreValidationResult(true, Math.Round(score.Value, 1), null);
    }

    private static bool HasExactlyOneDecimalPlace(string rawText)
    {
        var decimalSeparator = rawText.IndexOf('.', StringComparison.Ordinal);
        return decimalSeparator >= 0 && rawText.Length - decimalSeparator - 1 == 1;
    }

    private sealed record ScoreValidationResult(bool IsSuccess, double Score, string? Error);
}
