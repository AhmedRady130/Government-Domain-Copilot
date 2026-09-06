namespace GovernmentDomainCopilot.Application.Retrieval.Models;

public sealed record VectorSearchResponse
{
    public VectorSearchResponse(
        int topK,
        int totalReturned,
        TimeSpan duration,
        string providerName,
        string modelName,
        IReadOnlyList<VectorSearchResultItem> items)
    {
        TopK = topK;
        TotalReturned = totalReturned;
        Duration = duration;
        ProviderName = providerName;
        ModelName = modelName;
        Items = items;
    }

    public int TopK { get; }
    public int TotalReturned { get; }
    public TimeSpan Duration { get; }
    public string ProviderName { get; }
    public string ModelName { get; }
    public IReadOnlyList<VectorSearchResultItem> Items { get; }
}
