namespace GovernmentDomainCopilot.Application.Agents.Implementations;

using System.Diagnostics;
using System.Text.Json;
using GovernmentDomainCopilot.Application.Agents.Abstractions;
using GovernmentDomainCopilot.Application.Agents.Models;
using GovernmentDomainCopilot.Application.Observability;
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

        var queryMetadata = PromptDiagnosticMetadata.From(context.UserQuery);
        _logger.LogInformation(
            "Agent {Role} starting execution. QueryPresent={QueryPresent} QueryLength={QueryLength}",
            Role,
            queryMetadata.IsPresent,
            queryMetadata.Length);

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
        ToolExecutionResult toolResult;
        try
        {
            toolResult = await tool.ExecuteAsync(context, toolInput, cancellationToken);
        }
        catch (Exception)
        {
            toolResult = new ToolExecutionResult(false, "{}", ToolFailureCodes.ExecutionFailed);
        }
        toolStopwatch.Stop();

        var safeErrorCode = toolResult.Success ? null : ToolFailureCodes.Sanitize(toolResult.ErrorMessage);

        toolCalls.Add(new AgentToolCallRecord(
            tool.Name,
            System.Text.Json.JsonSerializer.Serialize(queryMetadata),
            System.Text.Json.JsonSerializer.Serialize(new { outputPresent = !string.IsNullOrWhiteSpace(toolResult.OutputJson), outputLength = toolResult.OutputJson?.Length ?? 0 }),
            toolResult.Success, toolStopwatch.Elapsed, safeErrorCode));

        if (!toolResult.Success)
        {
            return new AgentExecutionResult(
                Role, false, "Failed to retrieve eligibility information.",
                toolCalls, stopwatch.Elapsed, TerminateEarly: true,
                ErrorMessage: safeErrorCode);
        }

        context.SetState("EligibilityEvaluation", toolResult.OutputJson);
        stopwatch.Stop();

        return new AgentExecutionResult(
            Role,
            Success: true,
            Output: "Eligibility criteria evaluated from retrieved evidence.",
            ToolCalls: toolCalls,
            Duration: stopwatch.Elapsed,
            TerminateEarly: false);
    }
}
