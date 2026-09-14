using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GovernmentDomainCopilot.Application.Agents.Models;
using GovernmentDomainCopilot.Application.Answering.Models;
using GovernmentDomainCopilot.Application.Traces.Abstractions;
using GovernmentDomainCopilot.Infrastructure.Persistence;
using GovernmentDomainCopilot.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace GovernmentDomainCopilot.Infrastructure.Traces;

/// <summary>
/// PostgreSQL / EF Core backed durable implementation of <see cref="IRunTraceStore"/>.
/// Enforces server-side tenant scoping on every operation.
/// </summary>
public sealed class PostgresRunTraceStore : IRunTraceStore
{
    private readonly GovernmentDomainCopilotDbContext _dbContext;

    public PostgresRunTraceStore(GovernmentDomainCopilotDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task RecordRunAsync(
        OrchestrationRunRecord record,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var runToStore = !string.IsNullOrEmpty(sessionId) && record.SessionId != sessionId
            ? record with { SessionId = sessionId }
            : record;

        var finalResponseJson = runToStore.FinalResponse != null
            ? JsonSerializer.Serialize(runToStore.FinalResponse)
            : null;

        var agentExecutionsJson = runToStore.AgentExecutions.Count > 0
            ? JsonSerializer.Serialize(runToStore.AgentExecutions)
            : null;

        var pendingApprovalJson = runToStore.PendingApproval != null
            ? JsonSerializer.Serialize(runToStore.PendingApproval)
            : null;

        var entity = new OrchestrationRunTraceEntity
        {
            RunId = runToStore.RunId,
            TenantId = runToStore.TenantId,
            CorrelationId = runToStore.CorrelationId,
            SessionId = runToStore.SessionId,
            PatternName = runToStore.PatternName,
            Status = runToStore.Status,
            IterationCount = runToStore.IterationCount,
            Duration = runToStore.Duration,
            UsedFallback = runToStore.UsedFallback,
            FallbackReason = runToStore.FallbackReason,
            StartedAt = runToStore.StartedAt,
            CompletedAt = runToStore.CompletedAt,
            FinalResponseJson = finalResponseJson,
            AgentExecutionsJson = agentExecutionsJson,
            PendingApprovalJson = pendingApprovalJson,
            FailureReason = runToStore.FailureReason
        };

        _dbContext.OrchestrationRunTraces.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<OrchestrationRunRecord?> GetRunAsync(
        string runId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        var entity = await _dbContext.OrchestrationRunTraces
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.RunId == runId && r.TenantId == tenantId, cancellationToken);

        if (entity == null)
        {
            return null;
        }

        return MapToRecord(entity);
    }

    public async Task<IReadOnlyList<OrchestrationRunRecord>> ListRunsAsync(
        Guid tenantId,
        int skip = 0,
        int take = 50,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        if (skip < 0) skip = 0;
        if (take <= 0) take = 50;
        if (take > 100) take = 100;

        var query = _dbContext.OrchestrationRunTraces
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            query = query.Where(r => r.SessionId == sessionId);
        }

        var entities = await query
            .OrderByDescending(r => r.StartedAt)
            .ThenBy(r => r.RunId)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToRecord).ToList();
    }

    private static OrchestrationRunRecord MapToRecord(OrchestrationRunTraceEntity entity)
    {
        GroundedAnswerResponse? finalResponse = null;
        if (!string.IsNullOrWhiteSpace(entity.FinalResponseJson))
        {
            try
            {
                finalResponse = JsonSerializer.Deserialize<GroundedAnswerResponse>(entity.FinalResponseJson);
            }
            catch { }
        }

        IReadOnlyList<AgentExecutionRecord> agentExecutions = Array.Empty<AgentExecutionRecord>();
        if (!string.IsNullOrWhiteSpace(entity.AgentExecutionsJson))
        {
            try
            {
                agentExecutions = JsonSerializer.Deserialize<List<AgentExecutionRecord>>(entity.AgentExecutionsJson)
                    ?? (IReadOnlyList<AgentExecutionRecord>)Array.Empty<AgentExecutionRecord>();
            }
            catch { }
        }

        ApprovalRequest? pendingApproval = null;
        if (!string.IsNullOrWhiteSpace(entity.PendingApprovalJson))
        {
            try
            {
                pendingApproval = JsonSerializer.Deserialize<ApprovalRequest>(entity.PendingApprovalJson);
            }
            catch { }
        }

        return new OrchestrationRunRecord(
            RunId: entity.RunId,
            CorrelationId: entity.CorrelationId,
            TenantId: entity.TenantId,
            PatternName: entity.PatternName,
            StartedAt: entity.StartedAt,
            CompletedAt: entity.CompletedAt,
            Duration: entity.Duration,
            Status: entity.Status,
            IterationCount: entity.IterationCount,
            AgentExecutions: agentExecutions,
            UsedFallback: entity.UsedFallback,
            FallbackReason: entity.FallbackReason,
            FinalResponse: finalResponse,
            PendingApproval: pendingApproval,
            FailureReason: entity.FailureReason,
            SessionId: entity.SessionId);
    }
}
