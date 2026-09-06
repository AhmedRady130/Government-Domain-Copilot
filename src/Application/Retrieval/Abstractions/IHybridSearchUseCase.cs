namespace GovernmentDomainCopilot.Application.Retrieval.Abstractions;

using GovernmentDomainCopilot.Application.Retrieval.Models;

public interface IHybridSearchUseCase
{
    Task<HybridSearchResponse> SearchAsync(
        VectorSearchRequest request,
        CancellationToken cancellationToken);
}
