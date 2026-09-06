namespace GovernmentDomainCopilot.Application.Retrieval.Abstractions;

using GovernmentDomainCopilot.Application.Retrieval.Models;

/// <summary>
/// Deterministic retrieval reranker. Re-orders an already tenant-scoped, fused candidate set
/// using weighted retrieval signals. Must be stateless, synchronous, and produce identical output
/// for identical input.
/// </summary>
public interface IRetrievalReranker
{
    /// <summary>
    /// Reranks the fused candidates and returns a new ordered list with RerankScore and FinalRank populated.
    /// The caller is responsible for applying the final TopK slice.
    /// </summary>
    IReadOnlyList<RerankResultItem> Rerank(RerankRequest request);
}
