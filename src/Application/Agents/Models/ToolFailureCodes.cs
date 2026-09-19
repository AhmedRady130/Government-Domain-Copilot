namespace GovernmentDomainCopilot.Application.Agents.Models;

/// <summary>
/// Stable, non-sensitive categories for tool failures that may be persisted in
/// agent records and run traces. Never propagate arbitrary upstream error text.
/// </summary>
public static class ToolFailureCodes
{
    public const string ExecutionFailed = "ToolExecutionFailed";

    public static string Sanitize(string? errorMessage) => ExecutionFailed;
}
