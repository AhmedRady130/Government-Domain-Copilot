namespace GovernmentDomainCopilot.Application.Agents.Implementations;

using System.Diagnostics;
using System.Text.Json;
using GovernmentDomainCopilot.Application.Agents.Abstractions;
using GovernmentDomainCopilot.Application.Agents.Models;
using GovernmentDomainCopilot.Application.Observability;
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

        var queryMetadata = PromptDiagnosticMetadata.From(context.UserQuery);
        _logger.LogInformation(
            "Agent {Role} starting execution. QueryPresent={QueryPresent} QueryLength={QueryLength}",
            Role,
            queryMetadata.IsPresent,
            queryMetadata.Length);

        if (!availableTools.TryGetValue("procedure_lookup", out var tool) || !AllowedToolNames.Contains(tool.Name))
        {
            return new AgentExecutionResult(
                Role, false, "Allowed tool 'procedure_lookup' is not available.",
                toolCalls, stopwatch.Elapsed, TerminateEarly: true,
                ErrorMessage: "Required tool not found or unauthorized.");
        }

        var toolInput = JsonSerializer.Serialize(new { procedure = context.UserQuery });
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
                Role, false, "Failed to retrieve procedural information.",
                toolCalls, stopwatch.Elapsed, TerminateEarly: true,
                ErrorMessage: safeErrorCode);
        }

        context.SetState("ProcedureWorkflow", toolResult.OutputJson);
        stopwatch.Stop();

        return new AgentExecutionResult(
            Role,
            Success: true,
            Output: "Procedural steps and fees resolved from retrieved evidence.",
            ToolCalls: toolCalls,
            Duration: stopwatch.Elapsed,
            TerminateEarly: false);
    }
}
