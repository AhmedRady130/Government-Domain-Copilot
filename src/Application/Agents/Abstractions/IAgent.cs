namespace GovernmentDomainCopilot.Application.Agents.Abstractions;

using GovernmentDomainCopilot.Application.Agents.Models;

public interface IAgent
{
    string Role { get; }
    string Description { get; }
    IReadOnlySet<string> AllowedToolNames { get; }
    string InputContract { get; }
    string OutputContract { get; }
    string TerminationCondition { get; }

    Task<AgentExecutionResult> ExecuteAsync(
        AgentContext context,
        IReadOnlyDictionary<string, IAgentTool> availableTools,
        CancellationToken cancellationToken);
}
