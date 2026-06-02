using PhotoSelector.Ai.Ratings;

namespace PhotoSelector.Tests;

public sealed class AiRatingParserTests
{
    [Fact]
    public void Parse_reads_score_category_and_reason_from_valid_json()
    {
        var result = AiRatingParser.Parse("""{"score":4,"category":"keep","reason":"sharp subject"}""");

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Rating);
        Assert.Equal(4, result.Rating.Score);
        Assert.Equal("keep", result.Rating.Category);
        Assert.Equal("sharp subject", result.Rating.Reason);
        Assert.Null(result.Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Parse_fails_when_score_is_outside_one_through_five(int score)
    {
        var result = AiRatingParser.Parse($$"""{"score":{{score}},"category":"keep","reason":"sharp subject"}""");

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
        var result = AiRatingParser.Parse("""{"score":"4","category":"keep","reason":"sharp"}""");

        Assert.False(result.IsSuccess);
        Assert.Null(result.Rating);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void Parse_fails_when_category_is_unknown()
    {
        var result = AiRatingParser.Parse("""{"score":4,"category":"archive","reason":"sharp subject"}""");

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
}
