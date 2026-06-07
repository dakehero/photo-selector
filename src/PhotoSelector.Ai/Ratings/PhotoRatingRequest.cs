namespace PhotoSelector.Ai.Ratings;

public sealed record PhotoRatingRequest(
    Uri BaseUrl,
    string ApiKey,
    string Model,
    string Prompt,
    string ImagePath);
