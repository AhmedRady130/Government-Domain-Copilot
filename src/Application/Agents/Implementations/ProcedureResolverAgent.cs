namespace GovernmentDomainCopilot.Application.Agents.Implementations;

using System.Diagnostics;
using System.Text.Json;
using GovernmentDomainCopilot.Application.Agents.Abstractions;
using GovernmentDomainCopilot.Application.Agents.Models;
using Microsoft.Extensions.Logging;

public sealed class ProcedureResolverAgent : IAgent
{
    private readonly ILogger<ProcedureResolverAgent> _logger;

    public string Role => "Procedure Resolver Agent";
    public string Description => "Resolves procedural workflows, required documents, government fees, statutory deadlines, and submission venues.";
    public IReadOnlySet<string> AllowedToolNames { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "procedure_lookup",
        "document_search"
    };
    public string InputContract => "AgentContext containing UserQuery and optional upstream 'EligibilityEvaluation' state.";
    public string OutputContract => "AgentExecutionResult with resolved statutory procedural workflow, fees, and timelines stored in AgentContext state under 'ProcedureWorkflow'.";
    public string TerminationCondition => "Terminates when statutory procedural workflow, fees, and timelines are fully resolved and recorded in state.";

    public ProcedureResolverAgent(ILogger<ProcedureResolverAgent> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<AgentExecutionResult> ExecuteAsync(
        AgentContext context,
        IReadOnlyDictionary<string, IAgentTool> availableTools,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(availableTools);

        var stopwatch = Stopwatch.StartNew();
        var toolCalls = new List<AgentToolCallRecord>();

        _logger.LogInformation("Agent '{Role}' starting execution for query: {Query}", Role, context.UserQuery);

        if (!availableTools.TryGetValue("procedure_lookup", out var tool) || !AllowedToolNames.Contains(tool.Name))
        {
            return new AgentExecutionResult(
                Role, false, "Allowed tool 'procedure_lookup' is not available.",
                toolCalls, stopwatch.Elapsed, TerminateEarly: true,
                ErrorMessage: "Required tool not found or unauthorized.");
        }

        var toolInput = JsonSerializer.Serialize(new { procedure = context.UserQuery });
        var toolStopwatch = Stopwatch.StartNew();
        var toolResult = await tool.ExecuteAsync(context, toolInput, cancellationToken);
        toolStopwatch.Stop();

        toolCalls.Add(new AgentToolCallRecord(
            tool.Name, toolInput, toolResult.OutputJson,
            toolResult.Success, toolStopwatch.Elapsed, toolResult.ErrorMessage));

        if (!toolResult.Success)
        {
            return new AgentExecutionResult(
                Role, false, "Failed to retrieve procedural information.",
                toolCalls, stopwatch.Elapsed, TerminateEarly: true,
                ErrorMessage: toolResult.ErrorMessage);
        }

        context.SetState("ProcedureWorkflow", toolResult.OutputJson);
        stopwatch.Stop();

        return new AgentExecutionResult(
            Role,
            Success: true,
            Output: $"Procedural steps and fees resolved. Evidence: {toolResult.OutputJson}",
            ToolCalls: toolCalls,
            Duration: stopwatch.Elapsed,
            TerminateEarly: false);
    }
}
