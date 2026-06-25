using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotoSelector.Ai.Reviews;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(GroupReviewResponseJson))]
[JsonSerializable(typeof(RedactedGroupReviewRequestJson))]
internal sealed partial class ReviewJsonContext : JsonSerializerContext;

internal sealed record GroupReviewResponseJson(
    [property: JsonPropertyName("winner_base_name")] string? WinnerBaseName,
    string? Reason,
    GroupReviewItemDecisionJson[]? Items);

internal sealed record GroupReviewItemDecisionJson(
    [property: JsonPropertyName("base_name")] string? BaseName,
    string? Verdict,
    string? Reason);

internal sealed record RedactedGroupReviewRequestJson(
    string Model,
    string Prompt,
    RedactedGroupReviewItemJson[] Items);

internal sealed record RedactedGroupReviewItemJson(
    [property: JsonPropertyName("photo_id")] long PhotoId,
    [property: JsonPropertyName("base_name")] string BaseName,
    [property: JsonPropertyName("image_path")] string ImagePath,
    [property: JsonPropertyName("image_url")] string ImageUrl);
