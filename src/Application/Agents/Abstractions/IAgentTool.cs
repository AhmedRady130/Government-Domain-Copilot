namespace GovernmentDomainCopilot.Application.Agents.Abstractions;

using GovernmentDomainCopilot.Application.Agents.Models;

public interface IAgentTool
{
    string Name { get; }
    string Description { get; }
    bool IsSideEffecting { get; }

    Task<ToolExecutionResult> ExecuteAsync(
        AgentContext context,
        string inputJson,
        CancellationToken cancellationToken);
}
