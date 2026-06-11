using System.Text.Json;
using ImageMagick;

namespace PhotoSelector.Ai.Ratings;

internal static class RatingRequestPayload
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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
        var body = new
        {
            model = request.Model,
            temperature = 0,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = request.Prompt },
                        new { type = "image_url", image_url = new { url = dataUrl } },
                    },
                },
            },
        };

        return JsonSerializer.Serialize(body, JsonOptions);
    }

    public static string CreateRedactedRequestJson(PhotoRatingRequest request)
    {
        var preview = request.Preview ?? PhotoPreviewOptions.Standard;
        var redacted = new
        {
            model = request.Model,
            prompt = request.Prompt,
            image_path = request.ImagePath,
            preview = new
            {
                max_edge = preview.MaxEdge,
                jpeg_quality = preview.JpegQuality,
                image_url = "[redacted-data-url]",
            },
        };

        return JsonSerializer.Serialize(redacted, JsonOptions);
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
