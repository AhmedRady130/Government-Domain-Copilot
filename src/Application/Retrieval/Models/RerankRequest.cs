namespace GovernmentDomainCopilot.Application.Retrieval.Models;

/// <summary>
/// Input contract for the retrieval reranker.
/// Contains the fused candidate set produced by RRF.
/// No TenantId is included — tenant isolation is enforced upstream in the retrieval branches.
/// </summary>
/// <param name="Candidates">Fused candidate list from RRF, ordered by RRF score.</param>
public sealed record RerankRequest(IReadOnlyList<HybridSearchResultItem> Candidates);
