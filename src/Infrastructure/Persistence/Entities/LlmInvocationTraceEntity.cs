using System;

namespace GovernmentDomainCopilot.Infrastructure.Persistence.Entities;

/// <summary>
/// Durable PostgreSQL EF Core entity representing a safe, inspectable LLM invocation trace.
/// Strictly enforces server-side tenant isolation via TenantId foreign key.
/// </summary>
public sealed class LlmInvocationTraceEntity
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string? RunId { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public string OperationType { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
    public TimeSpan Duration { get; set; }
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public int? TotalTokens { get; set; }
    public decimal? EstimatedCost { get; set; }
}
