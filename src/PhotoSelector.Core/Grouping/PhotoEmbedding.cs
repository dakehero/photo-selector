namespace PhotoSelector.Core.Grouping;

public sealed record PhotoEmbedding(string PhotoPath, IReadOnlyList<float> Vector);
