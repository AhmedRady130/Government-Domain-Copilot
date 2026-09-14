namespace GovernmentDomainCopilot.Application.Traces.Abstractions;

using GovernmentDomainCopilot.Application.Agents.Models;

/// <summary>
/// Framework-agnostic store for inspectable multi-agent orchestration runs.
/// All queries and operations are strictly tenant-scoped.
/// </summary>
public interface IRunTraceStore
{
    /// <summary>
    /// Records a completed or terminal orchestration run trace.
    /// </summary>
    Task RecordRunAsync(
        OrchestrationRunRecord record,
        string? sessionId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a run trace by its unique RunId within the authenticated tenant context.
    /// Returns null if the run does not exist or belongs to another tenant.
    /// </summary>
    Task<OrchestrationRunRecord?> GetRunAsync(
        string runId,
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists run traces for the authenticated tenant with pagination and optional session filtering.
    /// Ordered deterministically by StartedAt descending.
    /// </summary>
    Task<IReadOnlyList<OrchestrationRunRecord>> ListRunsAsync(
        Guid tenantId,
        int skip = 0,
        int take = 50,
        string? sessionId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all run traces associated with a given correlation ID for the tenant.
    /// </summary>
    Task<IReadOnlyList<OrchestrationRunRecord>> GetRunsByCorrelationIdAsync(
        string correlationId,
        Guid tenantId,
        CancellationToken cancellationToken = default);
}
