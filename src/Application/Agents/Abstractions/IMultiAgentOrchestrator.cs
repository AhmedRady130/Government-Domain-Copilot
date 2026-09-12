namespace GovernmentDomainCopilot.Application.Agents.Abstractions;

using GovernmentDomainCopilot.Application.Agents.Models;

public interface IMultiAgentOrchestrator
{
    string PatternName { get; }

    Task<OrchestrationRunRecord> OrchestrateAsync(
        string userQuery,
        string? correlationId = null,
        CancellationToken cancellationToken = default);
}
