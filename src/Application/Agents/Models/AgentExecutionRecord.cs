namespace GovernmentDomainCopilot.Application.Agents.Models;

public sealed record AgentExecutionRecord(
    string AgentRole,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    TimeSpan Duration,
    bool Success,
    IReadOnlyList<AgentToolCallRecord> ToolCalls,
    string OutputSummary,
    string? ErrorMessage = null);
