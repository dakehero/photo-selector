using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotoSelector.Ai.Ratings;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AiRatingCriterion[]))]
[JsonSerializable(typeof(AiRatingResponseJson))]
[JsonSerializable(typeof(ChatCompletionsResponseJson))]
[JsonSerializable(typeof(ChatCompletionsRequestJson))]
[JsonSerializable(typeof(RedactedRatingRequestJson))]
public sealed partial class RatingJsonContext : JsonSerializerContext;

public sealed record AiRatingResponseJson(
    [property: JsonPropertyName("photo_type")] string? PhotoType,
    RatingScore? Score,
    string? Category,
    AiRatingCriterionResponseJson[]? Criteria,
    string? Reason);

public sealed record AiRatingCriterionResponseJson(
    string? Name,
    RatingScore? Score,
    string? Comment);

[JsonConverter(typeof(RatingScoreJsonConverter))]
public readonly record struct RatingScore(double Value, string RawText);

public sealed record ChatCompletionsResponseJson(
    ChatCompletionChoiceJson[]? Choices);

public sealed record ChatCompletionChoiceJson(
    ChatCompletionResponseMessageJson? Message);

public sealed record ChatCompletionResponseMessageJson(
    ChatCompletionMessageContentJson Content);

[JsonConverter(typeof(ChatCompletionMessageContentJsonConverter))]
public readonly record struct ChatCompletionMessageContentJson(string? Text);

public sealed record ChatCompletionsRequestJson(
    string Model,
    int Temperature,
    ChatMessageJson[] Messages);

public sealed record ChatMessageJson(
    string Role,
    ChatContentItemJson[] Content);

public sealed record ChatContentItemJson(
    string Type,
    string? Text = null,
    [property: JsonPropertyName("image_url")] ImageUrlJson? ImageUrl = null);

public sealed record ImageUrlJson(string Url);

public sealed record RedactedRatingRequestJson(
    string Model,
    string Prompt,
    [property: JsonPropertyName("image_path")] string ImagePath,
    RedactedPreviewJson Preview);

public sealed record RedactedPreviewJson(
    [property: JsonPropertyName("max_edge")] int MaxEdge,
    [property: JsonPropertyName("jpeg_quality")] int JpegQuality,
    [property: JsonPropertyName("image_url")] string ImageUrl);
