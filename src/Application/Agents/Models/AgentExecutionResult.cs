namespace GovernmentDomainCopilot.Application.Agents.Models;

public sealed record AgentExecutionResult(
    string AgentRole,
    bool Success,
    string Output,
    IReadOnlyList<AgentToolCallRecord> ToolCalls,
    TimeSpan Duration,
    bool TerminateEarly = false,
    string? ErrorMessage = null);
