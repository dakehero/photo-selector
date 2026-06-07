namespace PhotoSelector.Ai.Ratings;

public interface IPhotoRatingClient : IDisposable
{
    Task<AiRatingClientResult> RatePhotoAsync(PhotoRatingRequest request, CancellationToken cancellationToken);
}
