using PhotoSelector.Core.Grouping;

namespace PhotoSelector.Tests;

public sealed class PhotoGroupingEncoderTests
{
    [Fact]
    public async Task Encoder_contract_exposes_model_name_and_photo_embeddings_for_future_grouping_stage()
    {
        IPhotoGroupingEncoder encoder = new StubEncoder();

        Assert.Equal("stub-encoder", encoder.ModelName);
        var embedding = Assert.Single(await encoder.EncodeAsync(["IMG_0001.JPG"], CancellationToken.None));
        Assert.Equal("IMG_0001.JPG", embedding.PhotoPath);
        Assert.Equal([0.1f, 0.2f], embedding.Vector);
    }

    private sealed class StubEncoder : IPhotoGroupingEncoder
    {
        public string ModelName => "stub-encoder";

        public Task<IReadOnlyList<PhotoEmbedding>> EncodeAsync(
            IReadOnlyList<string> photoPaths,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<PhotoEmbedding> embeddings =
            [
                new(photoPaths[0], [0.1f, 0.2f]),
            ];
            return Task.FromResult(embeddings);
        }
    }
}
