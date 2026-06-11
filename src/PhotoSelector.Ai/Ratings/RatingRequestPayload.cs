using System.Text.Json;
using ImageMagick;

namespace PhotoSelector.Ai.Ratings;

internal static class RatingRequestPayload
{
    public static async Task<string> CreateJpegDataUrlAsync(string imagePath, CancellationToken cancellationToken)
    {
        return await CreateJpegDataUrlAsync(imagePath, PhotoPreviewOptions.Standard, cancellationToken);
    }

    public static async Task<string> CreateJpegDataUrlAsync(
        string imagePath,
        PhotoPreviewOptions preview,
        CancellationToken cancellationToken)
    {
        var imageBytes = await CreateJpegPreviewAsync(imagePath, preview, cancellationToken);
        return $"data:image/jpeg;base64,{Convert.ToBase64String(imageBytes)}";
    }

    public static string CreateChatCompletionsRequestJson(PhotoRatingRequest request, string dataUrl)
    {
        var body = new ChatCompletionsRequestJson(
            request.Model,
            0,
            [
                new ChatMessageJson(
                    "user",
                    [
                        new ChatContentItemJson("text", Text: request.Prompt),
                        new ChatContentItemJson("image_url", ImageUrl: new ImageUrlJson(dataUrl)),
                    ]),
            ]);

        return JsonSerializer.Serialize(body, RatingJsonContext.Default.ChatCompletionsRequestJson);
    }

    public static string CreateRedactedRequestJson(PhotoRatingRequest request)
    {
        var preview = request.Preview ?? PhotoPreviewOptions.Standard;
        var redacted = new RedactedRatingRequestJson(
            request.Model,
            request.Prompt,
            request.ImagePath,
            new RedactedPreviewJson(preview.MaxEdge, preview.JpegQuality, "[redacted-data-url]"));

        return JsonSerializer.Serialize(redacted, RatingJsonContext.Default.RedactedRatingRequestJson);
    }

    private static async Task<byte[]> CreateJpegPreviewAsync(string imagePath, CancellationToken cancellationToken)
    {
        return await CreateJpegPreviewAsync(imagePath, PhotoPreviewOptions.Standard, cancellationToken);
    }

    private static async Task<byte[]> CreateJpegPreviewAsync(
        string imagePath,
        PhotoPreviewOptions preview,
        CancellationToken cancellationToken)
    {
        await using var input = File.OpenRead(imagePath);
        using var image = new MagickImage(input);
        image.AutoOrient();

        var maxEdge = (uint)preview.MaxEdge;
        if (image.Width > maxEdge || image.Height > maxEdge)
        {
            image.Resize(new MagickGeometry(maxEdge, maxEdge)
            {
                IgnoreAspectRatio = false,
            });
        }

        image.Format = MagickFormat.Jpeg;
        image.Quality = (uint)preview.JpegQuality;
        return await Task.Run(image.ToByteArray, cancellationToken);
    }
}
