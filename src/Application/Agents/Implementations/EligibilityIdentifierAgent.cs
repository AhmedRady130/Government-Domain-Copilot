namespace GovernmentDomainCopilot.Application.Agents.Implementations;

using System.Diagnostics;
using System.Text.Json;
using GovernmentDomainCopilot.Application.Agents.Abstractions;
using GovernmentDomainCopilot.Application.Agents.Models;
using Microsoft.Extensions.Logging;

public sealed class EligibilityIdentifierAgent : IAgent
{
    private readonly ILogger<EligibilityIdentifierAgent> _logger;

    public string Role => "Eligibility Identifier Agent";
    public string Description => "Analyzes citizen or applicant eligibility, statutory prerequisites, exemptions, and disqualifications.";
    public IReadOnlySet<string> AllowedToolNames { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "eligibility_lookup",
        "document_search"
    };
    public string InputContract => "AgentContext containing the citizen or applicant inquiry in UserQuery.";
    public string OutputContract => "AgentExecutionResult with evaluated eligibility requirements stored in AgentContext state under 'EligibilityEvaluation'.";
    public string TerminationCondition => "Terminates when eligibility requirements and prerequisite conditions are extracted and recorded in state, or evidence indicates non-applicability.";

    public EligibilityIdentifierAgent(ILogger<EligibilityIdentifierAgent> logger)
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

        // Security / Contract check: Only allowed tools can be invoked
        if (!availableTools.TryGetValue("eligibility_lookup", out var tool) || !AllowedToolNames.Contains(tool.Name))
        {
            return new AgentExecutionResult(
                Role, false, "Allowed tool 'eligibility_lookup' is not available.",
                toolCalls, stopwatch.Elapsed, TerminateEarly: true,
                ErrorMessage: "Required tool not found or unauthorized.");
        }

        var toolInput = JsonSerializer.Serialize(new { topic = context.UserQuery });
        var toolStopwatch = Stopwatch.StartNew();
        var toolResult = await tool.ExecuteAsync(context, toolInput, cancellationToken);
        toolStopwatch.Stop();

        toolCalls.Add(new AgentToolCallRecord(
            tool.Name, toolInput, toolResult.OutputJson,
            toolResult.Success, toolStopwatch.Elapsed, toolResult.ErrorMessage));

        if (!toolResult.Success)
        {
            return new AgentExecutionResult(
                Role, false, "Failed to retrieve eligibility information.",
                toolCalls, stopwatch.Elapsed, TerminateEarly: true,
                ErrorMessage: toolResult.ErrorMessage);
        }

        context.SetState("EligibilityEvaluation", toolResult.OutputJson);
        stopwatch.Stop();

        return new AgentExecutionResult(
            Role,
            Success: true,
            Output: $"Eligibility criteria evaluated. Evidence: {toolResult.OutputJson}",
            ToolCalls: toolCalls,
            Duration: stopwatch.Elapsed,
            TerminateEarly: false);
    }
}
