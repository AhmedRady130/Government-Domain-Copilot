using System;
using System.Collections.Generic;

namespace GovernmentDomainCopilot.Infrastructure.Persistence.Entities;

public sealed class ConversationSessionEntity
{
    public string SessionId { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastActivityAt { get; set; }
    public string Status { get; set; } = "Active";

    public ICollection<SessionMessageEntity> Messages { get; set; } = new List<SessionMessageEntity>();
}

public sealed class SessionMessageEntity
{
    public string MessageId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? CitationsJson { get; set; }
    public string? LinkedRunId { get; set; }
    public DateTimeOffset Timestamp { get; set; }

    public ConversationSessionEntity? Session { get; set; }
}

public sealed class OrchestrationRunTraceEntity
{
    public string RunId { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string? SessionId { get; set; }
    public string PatternName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int IterationCount { get; set; }
    public TimeSpan Duration { get; set; }
    public bool UsedFallback { get; set; }
    public string? FallbackReason { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
    public string? FinalResponseJson { get; set; }
    public string? AgentExecutionsJson { get; set; }
    public string? PendingApprovalJson { get; set; }
    public string? FailureReason { get; set; }
}
