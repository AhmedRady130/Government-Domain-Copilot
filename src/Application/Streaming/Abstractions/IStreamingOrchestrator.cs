namespace GovernmentDomainCopilot.Application.Streaming.Abstractions;

using GovernmentDomainCopilot.Application.Streaming.Models;

public interface IStreamingOrchestrator
{
    IAsyncEnumerable<StreamProgressEvent> OrchestrateStreamAsync(
        string userQuery,
        string? correlationId = null,
        CancellationToken cancellationToken = default);
}
