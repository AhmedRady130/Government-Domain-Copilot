using System;
using GovernmentDomainCopilot.Application.Abstractions;

namespace GovernmentDomainCopilot.Infrastructure.Observability;

/// <summary>
/// Scoped correlation context implementation.
/// Ensures a correlation ID is always available throughout the lifetime of the scope.
/// </summary>
public sealed class CorrelationContext : ICorrelationContext
{
    private string? _correlationId;

    public string CorrelationId
    {
        get => _correlationId ??= $"corr-{Guid.NewGuid():N}";
        set => _correlationId = value;
    }

    public void SetCorrelationId(string correlationId)
    {
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            _correlationId = correlationId;
        }
    }
}
