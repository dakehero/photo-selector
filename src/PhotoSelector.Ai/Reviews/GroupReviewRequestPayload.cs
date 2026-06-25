using System.Text.Json;
using PhotoSelector.Ai.Ratings;

namespace PhotoSelector.Ai.Reviews;

internal static class GroupReviewRequestPayload
{
    public static async Task<string[]> CreateJpegDataUrlsAsync(
        GroupReviewRequest request,
        CancellationToken cancellationToken)
    {
        var preview = request.Preview ?? PhotoPreviewOptions.Standard;
        var dataUrls = new string[request.Items.Count];
        for (var index = 0; index < request.Items.Count; index++)
        {
            var item = request.Items[index];
            if (!File.Exists(item.ImagePath))
            {
                throw new FileNotFoundException($"Image not found: {item.ImagePath}", item.ImagePath);
            }

            dataUrls[index] = await RatingRequestPayload.CreateJpegDataUrlAsync(
                item.ImagePath,
                preview,
                cancellationToken);
        }

        return dataUrls;
    }

    public static string CreateChatCompletionsRequestJson(GroupReviewRequest request, IReadOnlyList<string> dataUrls)
    {
        var content = new List<ChatContentItemJson>
        {
            new("text", Text: request.Prompt),
        };

        for (var index = 0; index < request.Items.Count; index++)
        {
            var item = request.Items[index];
            content.Add(new ChatContentItemJson("text", Text: $"Candidate: {item.BaseName}"));
            content.Add(new ChatContentItemJson("image_url", ImageUrl: new ImageUrlJson(dataUrls[index])));
        }

        var body = new ChatCompletionsRequestJson(
            request.Model,
            0,
            [new ChatMessageJson("user", content.ToArray())]);

        return JsonSerializer.Serialize(body, RatingJsonContext.Default.ChatCompletionsRequestJson);
    }

    public static string CreateRedactedRequestJson(GroupReviewRequest request)
    {
        var redacted = new RedactedGroupReviewRequestJson(
            request.Model,
            request.Prompt,
            request.Items
                .OrderBy(item => item.Order)
                .Select(item => new RedactedGroupReviewItemJson(
                    item.PhotoId,
                    item.BaseName,
                    item.ImagePath,
                    "[redacted-data-url]"))
                .ToArray());

        return JsonSerializer.Serialize(redacted, ReviewJsonContext.Default.RedactedGroupReviewRequestJson);
    }
}
