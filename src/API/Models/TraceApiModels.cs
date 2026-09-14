namespace GovernmentDomainCopilot.API.Models;

/// <summary>Response model for a single LLM invocation trace.</summary>
public sealed record LlmTraceApiResponse(
    Guid Id,
    Guid TenantId,
    string CorrelationId,
    string? RunId,
    string ProviderName,
    string ModelName,
    string OperationType,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    double DurationMs,
    bool IsSuccess,
    int? PromptTokens,
    int? CompletionTokens,
    int? TotalTokens,
    decimal? EstimatedCost,
    string? ErrorMessage);
