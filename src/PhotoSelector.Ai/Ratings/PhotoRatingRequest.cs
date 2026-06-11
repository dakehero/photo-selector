namespace PhotoSelector.Ai.Ratings;

public sealed record PhotoRatingRequest(
    Uri BaseUrl,
    string ApiKey,
    string Model,
    string Prompt,
    string ImagePath,
    PhotoPreviewOptions? Preview = null);

public sealed record PhotoPreviewOptions(int MaxEdge, int JpegQuality)
{
    public static PhotoPreviewOptions Fast { get; } = new(1280, 75);

    public static PhotoPreviewOptions Standard { get; } = new(1600, 82);

    public static PhotoPreviewOptions High { get; } = new(2048, 90);

    public static PhotoPreviewOptions Detail { get; } = new(3072, 92);
}
