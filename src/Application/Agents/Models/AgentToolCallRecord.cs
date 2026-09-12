namespace GovernmentDomainCopilot.Application.Agents.Models;

public sealed record AgentToolCallRecord(
    string ToolName,
    string InputJson,
    string OutputJson,
    bool Success,
    TimeSpan Duration,
    string? ErrorMessage = null);
