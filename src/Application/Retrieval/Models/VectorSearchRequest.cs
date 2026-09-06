namespace GovernmentDomainCopilot.Application.Retrieval.Models;

public sealed record VectorSearchRequest
{
    public VectorSearchRequest(string query, int? topK = null)
    {
        Query = query;
        TopK = topK;
    }

    public string Query { get; }
    public int? TopK { get; }
}
