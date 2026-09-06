namespace GovernmentDomainCopilot.Application.Embeddings.Models;

/// <summary>
/// Represents the completed output of an embedding generation operation.
/// </summary>
public sealed record EmbeddingResult
{
    public EmbeddingResult(
        string providerName,
        string modelName,
        int dimension,
        IReadOnlyList<EmbeddingItem> items,
        TimeSpan duration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimension);
        ArgumentNullException.ThrowIfNull(items);

        ProviderName = providerName;
        ModelName = modelName;
        Dimension = dimension;
        Items = items;
        Duration = duration;
    }

    public string ProviderName { get; }
    public string ModelName { get; }
    public int Dimension { get; }
    public IReadOnlyList<EmbeddingItem> Items { get; }
    public TimeSpan Duration { get; }
}
