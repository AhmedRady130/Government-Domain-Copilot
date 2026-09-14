using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GovernmentDomainCopilot.Application.Observability.Abstractions;
using GovernmentDomainCopilot.Application.Observability.Models;
using GovernmentDomainCopilot.Infrastructure.Persistence;
using GovernmentDomainCopilot.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace GovernmentDomainCopilot.Infrastructure.Observability;

/// <summary>
/// PostgreSQL / EF Core backed durable implementation of <see cref="ILlmTraceStore"/>.
/// Enforces server-side tenant isolation on every read and write operation.
/// </summary>
public sealed class PostgresLlmTraceStore : ILlmTraceStore
{
    private readonly GovernmentDomainCopilotDbContext _dbContext;

    public PostgresLlmTraceStore(GovernmentDomainCopilotDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task RecordTraceAsync(LlmInvocationTrace trace, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trace);

        var entity = new LlmInvocationTraceEntity
        {
            Id = trace.Id == Guid.Empty ? Guid.NewGuid() : trace.Id,
            TenantId = trace.TenantId,
            CorrelationId = trace.CorrelationId,
            RunId = trace.RunId,
            ProviderName = trace.ProviderName,
            ModelName = trace.ModelName,
            OperationType = trace.OperationType,
            StartedAt = trace.StartedAt,
            CompletedAt = trace.CompletedAt,
            Duration = trace.Duration,
            IsSuccess = trace.IsSuccess,
            ErrorMessage = trace.ErrorMessage,
            PromptTokens = trace.PromptTokens,
            CompletionTokens = trace.CompletionTokens,
            TotalTokens = trace.TotalTokens,
            EstimatedCost = trace.EstimatedCost
        };

        _dbContext.LlmInvocationTraces.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<LlmInvocationTrace>> GetTracesByCorrelationIdAsync(
        string correlationId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            return Array.Empty<LlmInvocationTrace>();
        }

        var entities = await _dbContext.LlmInvocationTraces
            .AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.CorrelationId == correlationId)
            .OrderBy(t => t.StartedAt)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToDomain).ToList();
    }

    public async Task<IReadOnlyList<LlmInvocationTrace>> GetTracesByRunIdAsync(
        string runId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return Array.Empty<LlmInvocationTrace>();
        }

        var entities = await _dbContext.LlmInvocationTraces
            .AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.RunId == runId)
            .OrderBy(t => t.StartedAt)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToDomain).ToList();
    }

    private static LlmInvocationTrace MapToDomain(LlmInvocationTraceEntity entity) => new(
        entity.Id,
        entity.TenantId,
        entity.CorrelationId,
        entity.RunId,
        entity.ProviderName,
        entity.ModelName,
        entity.OperationType,
        entity.StartedAt,
        entity.CompletedAt,
        entity.Duration,
        entity.IsSuccess,
        entity.PromptTokens,
        entity.CompletionTokens,
        entity.TotalTokens,
        entity.EstimatedCost,
        entity.ErrorMessage);
}
