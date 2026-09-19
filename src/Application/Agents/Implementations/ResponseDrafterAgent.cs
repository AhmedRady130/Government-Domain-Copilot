namespace GovernmentDomainCopilot.Application.Agents.Implementations;

using System.Diagnostics;
using System.Text.Json;
using GovernmentDomainCopilot.Application.Agents.Abstractions;
using GovernmentDomainCopilot.Application.Agents.Models;
using GovernmentDomainCopilot.Application.Answering.Abstractions;
using GovernmentDomainCopilot.Application.Answering.Models;
using Microsoft.Extensions.Logging;

public sealed class ResponseDrafterAgent : IAgent
{
    private readonly IGroundedAnswerUseCase _groundedAnswerUseCase;
    private readonly ILogger<ResponseDrafterAgent> _logger;

    public string Role => "Response Drafter Agent";
    public string Description => "Synthesizes eligibility and procedural findings into a grounded government response with exact citations, then submits for human supervisor approval via side-effecting tool.";
    public IReadOnlySet<string> AllowedToolNames { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "create_draft_approval"
    };
    public string InputContract => "AgentContext containing UserQuery and findings from upstream agents ('EligibilityEvaluation', 'ProcedureWorkflow').";
    public string OutputContract => "AgentExecutionResult with grounded government response, citations, and queued human supervisor approval request.";
    public string TerminationCondition => "Terminates when grounded response is synthesized, citations are validated, and the human supervisor approval request has been queued.";

    public ResponseDrafterAgent(
        IGroundedAnswerUseCase groundedAnswerUseCase,
        ILogger<ResponseDrafterAgent> logger)
    {
        _groundedAnswerUseCase = groundedAnswerUseCase ?? throw new ArgumentNullException(nameof(groundedAnswerUseCase));
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

        _logger.LogInformation("Agent '{Role}' generating grounded response draft...", Role);

        // Grounded answer pipeline execution (preserves all grounding, refusal, and citation invariants)
        var groundedRequest = new GroundedAnswerRequest(
            context.UserQuery,
            CorrelationId: context.CorrelationId,
            RunId: context.RunId);
        var groundedResponse = await _groundedAnswerUseCase.GetGroundedAnswerAsync(
            groundedRequest,
            context.EventSink,
            cancellationToken);

        context.SetState("GroundedAnswerResponse", groundedResponse);

        if (groundedResponse.Status == GroundedAnswerStatus.Refused)
        {
            stopwatch.Stop();
            return new AgentExecutionResult(
                Role,
                Success: true,
                Output: $"Refusal produced: {groundedResponse.Reason}",
                ToolCalls: toolCalls,
                Duration: stopwatch.Elapsed,
                TerminateEarly: true);
        }

        // If grounded answer succeeded, invoke the side-effecting DraftApprovalTool
        if (availableTools.TryGetValue("create_draft_approval", out var tool) && AllowedToolNames.Contains(tool.Name))
        {
            var toolInput = JsonSerializer.Serialize(new
            {
                proposedAction = "Publish Official Government Advisory",
                draftContent = groundedResponse.Answer
            });

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
                tool.Name, toolInput, toolResult.OutputJson,
                toolResult.Success, toolStopwatch.Elapsed, safeErrorCode));

            if (!toolResult.Success)
            {
                stopwatch.Stop();
                return new AgentExecutionResult(
                    Role, false, "Failed to submit draft for human approval.",
                    toolCalls, stopwatch.Elapsed, TerminateEarly: true,
                    ErrorMessage: safeErrorCode);
            }
        }
        else
        {
            stopwatch.Stop();
            return new AgentExecutionResult(
                Role, false, "Side-effecting tool 'create_draft_approval' is missing or unauthorized.",
                toolCalls, stopwatch.Elapsed, TerminateEarly: true,
                ErrorMessage: "Approval tool not found.");
        }

        stopwatch.Stop();
        return new AgentExecutionResult(
            Role,
            Success: true,
            Output: groundedResponse.Answer ?? string.Empty,
            ToolCalls: toolCalls,
            Duration: stopwatch.Elapsed,
            TerminateEarly: false);
    }
}
