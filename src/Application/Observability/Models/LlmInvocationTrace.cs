using System;

namespace GovernmentDomainCopilot.Application.Observability.Models;

/// <summary>
/// Represents an inspectable, safe trace record of an individual LLM invocation.
/// Strictly excludes sensitive personal data, user secrets, system prompts, and raw document chunks.
/// </summary>
public sealed record LlmInvocationTrace(
    Guid Id,
    Guid TenantId,
    string CorrelationId,
    string? RunId,
    string ProviderName,
    string ModelName,
    string OperationType,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    TimeSpan Duration,
    bool IsSuccess,
    int? PromptTokens,
    int? CompletionTokens,
    int? TotalTokens,
    decimal? EstimatedCost,
    string? ErrorMessage = null);
