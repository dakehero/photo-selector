using System.Text.Json;
using ImageMagick;

namespace PhotoSelector.Ai.Ratings;

internal static class RatingRequestPayload
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const int MaxPreviewEdge = 1600;
    private const int PreviewJpegQuality = 82;

    public static async Task<string> CreateJpegDataUrlAsync(string imagePath, CancellationToken cancellationToken)
    {
        var imageBytes = await CreateJpegPreviewAsync(imagePath, cancellationToken);
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
        var redacted = new
        {
            model = request.Model,
            prompt = request.Prompt,
            image_path = request.ImagePath,
            preview = new
            {
                max_edge = MaxPreviewEdge,
                jpeg_quality = PreviewJpegQuality,
                image_url = "[redacted-data-url]",
            },
        };

        return JsonSerializer.Serialize(redacted, JsonOptions);
    }

    private static async Task<byte[]> CreateJpegPreviewAsync(string imagePath, CancellationToken cancellationToken)
    {
        await using var input = File.OpenRead(imagePath);
        using var image = new MagickImage(input);
        image.AutoOrient();

        if (image.Width > MaxPreviewEdge || image.Height > MaxPreviewEdge)
        {
            image.Resize(new MagickGeometry(MaxPreviewEdge, MaxPreviewEdge)
            {
                IgnoreAspectRatio = false,
            });
        }

        image.Format = MagickFormat.Jpeg;
        image.Quality = PreviewJpegQuality;
        return await Task.Run(image.ToByteArray, cancellationToken);
    }
}
