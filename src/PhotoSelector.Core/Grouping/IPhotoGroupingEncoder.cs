namespace PhotoSelector.Core.Grouping;

public interface IPhotoGroupingEncoder
{
    string ModelName { get; }

    Task<IReadOnlyList<PhotoEmbedding>> EncodeAsync(
        IReadOnlyList<string> photoPaths,
        CancellationToken cancellationToken);
}
