using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GovernmentDomainCopilot.Application.Observability.Abstractions;
using GovernmentDomainCopilot.Application.Observability.Models;

namespace GovernmentDomainCopilot.Application.Observability.Services;

/// <summary>
/// In-memory trace store for unit test scenarios only.
/// Runtime execution uses PostgreSQL persistence.
/// </summary>
public sealed class InMemoryLlmTraceStore : ILlmTraceStore
{
    private readonly ConcurrentBag<LlmInvocationTrace> _traces = new();

    public Task RecordTraceAsync(LlmInvocationTrace trace, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trace);
        _traces.Add(trace);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<LlmInvocationTrace>> GetTracesByCorrelationIdAsync(
        string correlationId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var matches = _traces
            .Where(t => t.TenantId == tenantId && string.Equals(t.CorrelationId, correlationId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.StartedAt)
            .ToList();

        return Task.FromResult<IReadOnlyList<LlmInvocationTrace>>(matches);
    }

    public Task<IReadOnlyList<LlmInvocationTrace>> GetTracesByRunIdAsync(
        string runId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var matches = _traces
            .Where(t => t.TenantId == tenantId && string.Equals(t.RunId, runId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.StartedAt)
            .ToList();

        return Task.FromResult<IReadOnlyList<LlmInvocationTrace>>(matches);
    }
}
