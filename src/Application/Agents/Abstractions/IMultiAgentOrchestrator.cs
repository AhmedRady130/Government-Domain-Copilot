namespace GovernmentDomainCopilot.Application.Agents.Abstractions;

using GovernmentDomainCopilot.Application.Agents.Models;
using GovernmentDomainCopilot.Application.Streaming.Abstractions;

public interface IMultiAgentOrchestrator : IStreamingOrchestrator
{
    string PatternName { get; }

    Task<OrchestrationRunRecord> OrchestrateAsync(
        string userQuery,
        string? correlationId = null,
        CancellationToken cancellationToken = default);
}
