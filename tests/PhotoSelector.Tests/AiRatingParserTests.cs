using PhotoSelector.Ai.Ratings;

namespace PhotoSelector.Tests;

public sealed class AiRatingParserTests
{
    [Fact]
    public void Ai_rating_json_contracts_use_serializer_dtos_instead_of_hand_written_json_field_walks()
    {
        var ratingsDirectory = FindRepositoryDirectory("src/PhotoSelector.Ai/Ratings");
        var allowedRawShapeConverter = Path.Combine(ratingsDirectory, "ChatCompletionMessageContentJsonConverter.cs");
        var disallowedPatterns = new[]
        {
            "TryGetProperty",
            "GetProperty",
            "EnumerateArray",
            "JsonDocument.Parse(",
            ".GetRawText()",
        };

        foreach (var sourcePath in Directory.EnumerateFiles(ratingsDirectory, "*.cs"))
        {
            if (string.Equals(sourcePath, allowedRawShapeConverter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var source = File.ReadAllText(sourcePath);
            foreach (var pattern in disallowedPatterns)
            {
                Assert.DoesNotContain(pattern, source, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void Parse_reads_type_score_category_criteria_and_reason_from_valid_json()
    {
        var result = AiRatingParser.Parse(
            """
            {
              "photo_type": "landscape",
              "score": 8.4,
              "category": "keep",
              "criteria": [
                {"name": "impact", "score": 7.3, "comment": "Strong atmosphere."},
                {"name": "composition", "score": 8.1, "comment": "Clear layered structure."}
              ],
              "reason": "Strong landscape with good atmosphere and structure."
            }
            """);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Rating);
        Assert.Equal("landscape", result.Rating.PhotoType);
        Assert.Equal(8.4, result.Rating.Score);
        Assert.Equal("keep", result.Rating.Category);
        Assert.Equal("Strong landscape with good atmosphere and structure.", result.Rating.Reason);
        Assert.Equal(2, result.Rating.Criteria.Count);
        Assert.Equal("impact", result.Rating.Criteria[0].Name);
        Assert.Equal(7.3, result.Rating.Criteria[0].Score);
        Assert.Equal("Strong atmosphere.", result.Rating.Criteria[0].Comment);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Parse_accepts_numeric_strings_with_one_decimal_place()
    {
        var result = AiRatingParser.Parse(
            """
            {
              "photo_type": "architecture",
              "score": "6.5",
              "category": "maybe",
              "criteria": [
                {"name": "impact", "score": "6.0", "comment": "Clear subject."},
                {"name": "composition", "score": "6.5", "comment": "Stable framing."}
              ],
              "reason": "Usable reference frame."
            }
            """);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Rating);
        Assert.Equal(6.5, result.Rating.Score);
        Assert.Equal(6.0, result.Rating.Criteria[0].Score);
        Assert.Equal(6.5, result.Rating.Criteria[1].Score);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void Parse_fails_when_score_is_outside_one_through_ten(int score)
    {
        var result = AiRatingParser.Parse($$"""
            {
              "photo_type": "landscape",
              "score": {{score}},
              "category": "keep",
              "criteria": [{"name": "impact", "score": 7, "comment": "Strong atmosphere."}],
              "reason": "sharp subject"
            }
            """);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Rating);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void Parse_fails_when_photo_type_is_missing()
    {
        var result = AiRatingParser.Parse("""{"score":8,"category":"keep","criteria":[],"reason":"sharp subject"}""");

        Assert.False(result.IsSuccess);
        Assert.Null(result.Rating);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void Parse_fails_when_criteria_score_is_outside_one_through_ten()
    {
        var result = AiRatingParser.Parse(
            """
            {
              "photo_type": "landscape",
              "score": 8,
              "category": "keep",
              "criteria": [{"name": "impact", "score": 11, "comment": "Too high."}],
              "reason": "sharp subject"
            }
            """);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Rating);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void Parse_fails_when_score_has_more_than_one_decimal_place()
    {
        var result = AiRatingParser.Parse(
            """
            {
              "photo_type": "landscape",
              "score": 8.25,
              "category": "keep",
              "criteria": [{"name": "impact", "score": 7.3, "comment": "Strong atmosphere."}],
              "reason": "sharp subject"
            }
            """);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Rating);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void Parse_fails_when_score_has_no_decimal_place()
    {
        var result = AiRatingParser.Parse(
            """
            {
              "photo_type": "landscape",
              "score": 8,
              "category": "keep",
              "criteria": [{"name": "impact", "score": 7.3, "comment": "Strong atmosphere."}],
              "reason": "sharp subject"
            }
            """);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Rating);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void Parse_fails_when_criterion_score_has_more_than_one_decimal_place()
    {
        var result = AiRatingParser.Parse(
            """
            {
              "photo_type": "landscape",
              "score": 8.2,
              "category": "keep",
              "criteria": [{"name": "impact", "score": 7.35, "comment": "Strong atmosphere."}],
              "reason": "sharp subject"
            }
            """);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Rating);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void Parse_fails_when_root_json_is_not_an_object()
    {
        var result = AiRatingParser.Parse("[]");

        Assert.False(result.IsSuccess);
        Assert.Null(result.Rating);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void Parse_fails_when_score_is_not_a_number()
    {
        var result = AiRatingParser.Parse(
            """{"photo_type":"landscape","score":"high","category":"keep","criteria":[],"reason":"sharp"}""");

        Assert.False(result.IsSuccess);
        Assert.Null(result.Rating);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Theory]
    [InlineData("8")]
    [InlineData("8.25")]
    public void Parse_fails_when_numeric_string_score_does_not_have_exactly_one_decimal_place(string score)
    {
        var result = AiRatingParser.Parse($$"""
            {
              "photo_type": "landscape",
              "score": "{{score}}",
              "category": "keep",
              "criteria": [{"name": "impact", "score": "7.3", "comment": "Strong atmosphere."}],
              "reason": "sharp subject"
            }
            """);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Rating);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void Parse_fails_when_category_is_unknown()
    {
        var result = AiRatingParser.Parse(
            """{"photo_type":"landscape","score":8,"category":"archive","criteria":[],"reason":"sharp subject"}""");

        Assert.False(result.IsSuccess);
        Assert.Null(result.Rating);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Theory]
    [InlineData(8.0, "maybe")]
    [InlineData(7.9, "keep")]
    [InlineData(4.9, "maybe")]
    public void Parse_fails_when_category_does_not_match_score(double score, string category)
    {
        var result = AiRatingParser.Parse($$"""
            {
              "photo_type": "landscape",
              "score": {{score}},
              "category": "{{category}}",
              "criteria": [{"name": "impact", "score": 7.0, "comment": "Clear atmosphere."}],
              "reason": "sharp subject"
            }
            """);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Rating);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void Parse_fails_when_json_is_malformed()
    {
        var result = AiRatingParser.Parse("""{"score":4,"category":"keep","reason":""");

        Assert.False(result.IsSuccess);
        Assert.Null(result.Rating);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    private static string FindRepositoryDirectory(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"Could not find repository directory: {relativePath}");
    }
}
