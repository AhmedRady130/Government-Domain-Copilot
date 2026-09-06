namespace GovernmentDomainCopilot.Application.Retrieval.Abstractions;

using GovernmentDomainCopilot.Application.Retrieval.Models;

public interface IVectorSearchUseCase
{
    Task<VectorSearchResponse> SearchAsync(
        VectorSearchRequest request,
        CancellationToken cancellationToken);
}
