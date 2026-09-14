using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GovernmentDomainCopilot.Application.Observability.Models;

namespace GovernmentDomainCopilot.Application.Observability.Abstractions;

/// <summary>
/// Framework-agnostic store for recording and querying LLM invocation traces.
/// All queries strictly enforce server-side tenant isolation.
/// </summary>
public interface ILlmTraceStore
{
    /// <summary>
    /// Records a completed or failed LLM invocation trace.
    /// </summary>
    Task RecordTraceAsync(LlmInvocationTrace trace, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all LLM invocation traces associated with a given correlation ID for the tenant.
    /// </summary>
    Task<IReadOnlyList<LlmInvocationTrace>> GetTracesByCorrelationIdAsync(
        string correlationId,
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all LLM invocation traces associated with a specific run ID for the tenant.
    /// </summary>
    Task<IReadOnlyList<LlmInvocationTrace>> GetTracesByRunIdAsync(
        string runId,
        Guid tenantId,
        CancellationToken cancellationToken = default);
}
