namespace GovernmentDomainCopilot.Application.Traces.Services;

using System.Collections.Concurrent;
using GovernmentDomainCopilot.Application.Agents.Models;
using GovernmentDomainCopilot.Application.Traces.Abstractions;

/// <summary>
/// Thread-safe in-memory implementation of <see cref="IRunTraceStore"/>.
/// Note: In-memory storage is non-durable and not suitable for multi-replica production deployments.
/// Designed for single-instance development, testing, and inspection.
/// </summary>
public sealed class InMemoryRunTraceStore : IRunTraceStore
{
    private readonly ConcurrentDictionary<string, OrchestrationRunRecord> _runs = new();

    public Task RecordRunAsync(
        OrchestrationRunRecord record,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        // If a sessionId was provided and record does not already have it, ensure it is set
        var runToStore = !string.IsNullOrEmpty(sessionId) && record.SessionId != sessionId
            ? record with { SessionId = sessionId }
            : record;

        _runs[runToStore.RunId] = runToStore;
        return Task.CompletedTask;
    }

    public Task<OrchestrationRunRecord?> GetRunAsync(
        string runId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        if (_runs.TryGetValue(runId, out var record) && record.TenantId == tenantId)
        {
            return Task.FromResult<OrchestrationRunRecord?>(record);
        }

        return Task.FromResult<OrchestrationRunRecord?>(null);
    }

    public Task<IReadOnlyList<OrchestrationRunRecord>> ListRunsAsync(
        Guid tenantId,
        int skip = 0,
        int take = 50,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        if (skip < 0) skip = 0;
        if (take <= 0) take = 50;
        if (take > 100) take = 100; // bound max page size

        var query = _runs.Values
            .Where(r => r.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            query = query.Where(r => string.Equals(r.SessionId, sessionId, StringComparison.OrdinalIgnoreCase));
        }

        var results = query
            .OrderByDescending(r => r.StartedAt)
            .ThenBy(r => r.RunId)
            .Skip(skip)
            .Take(take)
            .ToList();

        return Task.FromResult<IReadOnlyList<OrchestrationRunRecord>>(results);
    }
}
