namespace GovernmentDomainCopilot.Application.Agents.Services;

using System.Diagnostics;
using System.Threading.Channels;
using GovernmentDomainCopilot.Application.Abstractions;
using GovernmentDomainCopilot.Application.Agents.Abstractions;
using GovernmentDomainCopilot.Application.Agents.Models;
using GovernmentDomainCopilot.Application.Answering.Abstractions;
using GovernmentDomainCopilot.Application.Answering.Models;
using GovernmentDomainCopilot.Application.Streaming.Abstractions;
using GovernmentDomainCopilot.Application.Streaming.Models;
using GovernmentDomainCopilot.Application.Streaming.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public sealed class SequentialPipelineOrchestrator : IMultiAgentOrchestrator
{
    private readonly IEnumerable<IAgent> _agents;
    private readonly IEnumerable<IAgentTool> _tools;
    private readonly IGroundedAnswerUseCase _groundedAnswerUseCase;
    private readonly ITenantContext _tenantContext;
    private readonly OrchestrationOptions _options;
    private readonly ILogger<SequentialPipelineOrchestrator> _logger;

    private static readonly string[] PipelineRoles =
    {
        "Eligibility Identifier Agent",
        "Procedure Resolver Agent",
        "Response Drafter Agent"
    };

    public string PatternName => "Sequential Pipeline with Human-in-the-Loop & Plain-RAG Fallback";

    public SequentialPipelineOrchestrator(
        IEnumerable<IAgent> agents,
        IEnumerable<IAgentTool> tools,
        IGroundedAnswerUseCase groundedAnswerUseCase,
        ITenantContext tenantContext,
        IOptions<OrchestrationOptions> options,
        ILogger<SequentialPipelineOrchestrator> logger)
    {
        _agents = agents ?? throw new ArgumentNullException(nameof(agents));
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
        _groundedAnswerUseCase = groundedAnswerUseCase ?? throw new ArgumentNullException(nameof(groundedAnswerUseCase));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _options = options?.Value ?? new OrchestrationOptions();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async IAsyncEnumerable<StreamProgressEvent> OrchestrateStreamAsync(
        string userQuery,
        string? correlationId = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();
        var runId = $"run-{Guid.NewGuid():N}";
        var resolvedCorrelationId = correlationId ?? $"corr-{Guid.NewGuid():N}";

        var channel = Channel.CreateUnbounded<StreamProgressEvent>(
            new UnboundedChannelOptions
            {
                SingleWriter = true,
                SingleReader = true
            });

        var sink = new ChannelBasedEventSink(channel, runId, resolvedCorrelationId, tenantId);
        var context = new AgentContext(tenantId, runId, resolvedCorrelationId, userQuery, eventSink: sink);
        var toolsByName = _tools.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

        var totalStopwatch = Stopwatch.StartNew();

        _ = Task.Run(async () =>
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds)));
            var pipelineCt = cts.Token;

            try
            {
                sink.Emit(BuildEvent(runId, resolvedCorrelationId, tenantId,
                    StreamEventType.RunStarted,
                    "Pipeline", "Started", totalStopwatch.ElapsedMilliseconds));

                var orderedAgents = _agents
                    .OrderBy(a => Array.IndexOf(PipelineRoles, a.Role) >= 0 ? Array.IndexOf(PipelineRoles, a.Role) : int.MaxValue)
                    .ToList();

                int iterationCount = 0;
                foreach (var agent in orderedAgents)
                {
                    iterationCount++;
                    if (iterationCount > _options.MaxIterations)
                    {
                        throw new InvalidOperationException($"Maximum iteration limit ({_options.MaxIterations}) exceeded.");
                    }

                    pipelineCt.ThrowIfCancellationRequested();

                    sink.Emit(BuildEvent(runId, resolvedCorrelationId, tenantId,
                        StreamEventType.AgentStarted,
                        agent.Role, "Running", totalStopwatch.ElapsedMilliseconds,
                        agentRole: agent.Role));

                    var allowedTools = toolsByName
                        .Where(kvp => agent.AllowedToolNames.Contains(kvp.Key))
                        .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

                    var agentResult = await ExecuteAgentWithRetryAsync(agent, context, allowedTools, pipelineCt);

                    foreach (var tc in agentResult.ToolCalls)
                    {
                        sink.Emit(BuildEvent(runId, resolvedCorrelationId, tenantId,
                            StreamEventType.ToolStarted,
                            agent.Role, "Running", totalStopwatch.ElapsedMilliseconds,
                            agentRole: agent.Role,
                            toolName: tc.ToolName));

                        sink.Emit(BuildEvent(runId, resolvedCorrelationId, tenantId,
                            StreamEventType.ToolCompleted,
                            agent.Role, tc.Success ? "Completed" : "Failed", totalStopwatch.ElapsedMilliseconds,
                            agentRole: agent.Role,
                            toolName: tc.ToolName));
                    }

                    var pendingApprovalAfterAgent = context.GetState<ApprovalRequest>("PendingApprovalRequest");
                    if (pendingApprovalAfterAgent != null)
                    {
                        sink.Emit(BuildEvent(runId, resolvedCorrelationId, tenantId,
                            StreamEventType.ApprovalRequired,
                            agent.Role, "ApprovalPending", totalStopwatch.ElapsedMilliseconds,
                            agentRole: agent.Role,
                            approvalRequestId: pendingApprovalAfterAgent.RequestId,
                            approvalAction: pendingApprovalAfterAgent.ProposedAction));
                    }

                    if (!agentResult.Success)
                    {
                        if (_options.EnableFallback && !cancellationToken.IsCancellationRequested)
                        {
                            sink.Emit(BuildEvent(runId, resolvedCorrelationId, tenantId,
                                StreamEventType.FallbackStarted,
                                "Fallback", "Started", totalStopwatch.ElapsedMilliseconds,
                                message: "Orchestration failed; initiating Plain-RAG fallback."));

                            var fallbackResponse = await _groundedAnswerUseCase.GetGroundedAnswerAsync(
                                new GroundedAnswerRequest(userQuery),
                                sink,
                                CancellationToken.None);

                            if (fallbackResponse.Status == GroundedAnswerStatus.Grounded)
                            {
                                sink.Emit(BuildEvent(runId, resolvedCorrelationId, tenantId,
                                    StreamEventType.RunCompleted,
                                    "Pipeline", "CompletedWithFallback", totalStopwatch.ElapsedMilliseconds,
                                    finalResponse: fallbackResponse));
                                return;
                            }
                            else
                            {
                                sink.Emit(BuildEvent(runId, resolvedCorrelationId, tenantId,
                                    StreamEventType.RunFailed,
                                    "Pipeline", "Failed", totalStopwatch.ElapsedMilliseconds,
                                    errorMessage: TruncateErrorMessage(fallbackResponse.Reason ?? "Fallback answer refused.")));
                                return;
                            }
                        }

                        sink.Emit(BuildEvent(runId, resolvedCorrelationId, tenantId,
                            StreamEventType.RunFailed,
                            agent.Role, "Failed", totalStopwatch.ElapsedMilliseconds,
                            agentRole: agent.Role,
                            errorMessage: TruncateErrorMessage(agentResult.ErrorMessage)));
                        return;
                    }

                    if (agentResult.TerminateEarly)
                    {
                        sink.Emit(BuildEvent(runId, resolvedCorrelationId, tenantId,
                            StreamEventType.AgentProgress,
                            agent.Role, "TerminatedEarly", totalStopwatch.ElapsedMilliseconds,
                            agentRole: agent.Role,
                            message: "Agent signaled early termination."));
                        break;
                    }
                }

                var finalResponse = context.GetState<GroundedAnswerResponse>("GroundedAnswerResponse");
                var pendingApproval = context.GetState<ApprovalRequest>("PendingApprovalRequest");

                // Final-Answer Integrity: RunCompleted may ONLY represent a successfully validated response
                if (finalResponse != null && finalResponse.Status == GroundedAnswerStatus.Grounded)
                {
                    sink.Emit(BuildEvent(runId, resolvedCorrelationId, tenantId,
                        StreamEventType.RunCompleted,
                        "Pipeline", "Completed", totalStopwatch.ElapsedMilliseconds,
                        finalResponse: finalResponse,
                        approvalRequestId: pendingApproval?.RequestId,
                        approvalAction: pendingApproval?.ProposedAction));
                }
                else
                {
                    // Invalid citation or refusal output must NEVER be emitted as RunCompleted
                    var reason = finalResponse?.Reason ?? "Structural citation validation failed or answer refused.";
                    sink.Emit(BuildEvent(runId, resolvedCorrelationId, tenantId,
                        StreamEventType.RunFailed,
                        "Pipeline", "Refused", totalStopwatch.ElapsedMilliseconds,
                        finalResponse: finalResponse,
                        errorMessage: TruncateErrorMessage(reason)));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Explicit caller cancellation: emit RunCancelled, never fallback, never execute side effects
                sink.Emit(BuildEvent(runId, resolvedCorrelationId, tenantId,
                    StreamEventType.RunCancelled,
                    "Pipeline", "Cancelled", totalStopwatch.ElapsedMilliseconds,
                    message: "Orchestration was cancelled by caller."));
            }
            catch (OperationCanceledException)
            {
                // Timeout
                sink.Emit(BuildEvent(runId, resolvedCorrelationId, tenantId,
                    StreamEventType.RunFailed,
                    "Pipeline", "Timeout", totalStopwatch.ElapsedMilliseconds,
                    errorMessage: $"Orchestration timed out after {_options.TimeoutSeconds} seconds."));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Streaming orchestration failed for RunId={RunId}.", runId);
                sink.Emit(BuildEvent(runId, resolvedCorrelationId, tenantId,
                    StreamEventType.RunFailed,
                    "Pipeline", "Failed", totalStopwatch.ElapsedMilliseconds,
                    errorMessage: TruncateErrorMessage(ex.Message)));
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        }, CancellationToken.None);

        while (await channel.Reader.WaitToReadAsync(CancellationToken.None))
        {
            while (channel.Reader.TryRead(out var evt))
            {
                yield return evt;
                if (evt.EventType is StreamEventType.RunCompleted or StreamEventType.RunFailed or StreamEventType.RunCancelled)
                {
                    yield break;
                }
            }
        }
    }

    public async Task<OrchestrationRunRecord> OrchestrateAsync(
        string userQuery,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.GetTenantId();
        var runId = $"run-{Guid.NewGuid():N}";
        var resolvedCorrelationId = correlationId ?? $"corr-{Guid.NewGuid():N}";
        var startedAt = DateTimeOffset.UtcNow;
        var totalStopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "Starting multi-agent orchestration. RunId={RunId}, TenantId={TenantId}, Pattern={Pattern}",
            runId, tenantId, PatternName);

        var context = new AgentContext(tenantId, runId, resolvedCorrelationId, userQuery);
        var toolsByName = _tools.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        var agentExecutions = new List<AgentExecutionRecord>();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds)));

        int iterationCount = 0;
        bool orchestrationSuccess = false;
        string? failureReason = null;

        try
        {
            // Deterministic pipeline sequence: EligibilityIdentifierAgent -> ProcedureResolverAgent -> ResponseDrafterAgent
            var orderedAgents = _agents
                .OrderBy(a => Array.IndexOf(PipelineRoles, a.Role) >= 0 ? Array.IndexOf(PipelineRoles, a.Role) : int.MaxValue)
                .ToList();

            foreach (var agent in orderedAgents)
            {
                iterationCount++;
                if (iterationCount > _options.MaxIterations)
                {
                    throw new InvalidOperationException(
                        $"Maximum iteration limit ({_options.MaxIterations}) exceeded during orchestration.");
                }

                cts.Token.ThrowIfCancellationRequested();

                // Enforce tool allow-list security boundary
                var allowedToolsForAgent = toolsByName
                    .Where(kvp => agent.AllowedToolNames.Contains(kvp.Key))
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

                // Execute agent with bounded retries & exponential backoff
                AgentExecutionResult agentResult = await ExecuteAgentWithRetryAsync(
                    agent, context, allowedToolsForAgent, cts.Token);

                agentExecutions.Add(new AgentExecutionRecord(
                    agent.Role,
                    DateTimeOffset.UtcNow.Subtract(agentResult.Duration),
                    DateTimeOffset.UtcNow,
                    agentResult.Duration,
                    agentResult.Success,
                    agentResult.ToolCalls,
                    agentResult.Output,
                    agentResult.ErrorMessage));

                if (!agentResult.Success)
                {
                    throw new InvalidOperationException(
                        $"Agent '{agent.Role}' failed: {agentResult.ErrorMessage ?? "Unknown failure"}");
                }

                if (agentResult.TerminateEarly)
                {
                    _logger.LogInformation("Agent '{Role}' signaled early termination condition.", agent.Role);
                    break;
                }
            }

            orchestrationSuccess = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            failureReason = "Orchestration was cancelled by caller.";
        }
        catch (OperationCanceledException)
        {
            failureReason = $"Orchestration timed out after {_options.TimeoutSeconds} seconds.";
        }
        catch (Exception ex)
        {
            failureReason = $"Orchestration execution failed: {ex.Message}";
            _logger.LogWarning(ex, "Multi-agent orchestration failed for RunId={RunId}. Reason={Reason}", runId, failureReason);
        }

        totalStopwatch.Stop();

        // Check if an approval request was created
        var pendingApproval = context.GetState<ApprovalRequest>("PendingApprovalRequest");
        var finalResponse = context.GetState<GroundedAnswerResponse>("GroundedAnswerResponse");

        // Plain-RAG Fallback evaluation: Explicit caller cancellation must never trigger fallback
        bool usedFallback = false;
        string? fallbackReason = null;

        if (!orchestrationSuccess && _options.EnableFallback && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Orchestration failed for RunId={RunId}. Executing Plain-RAG fallback for tenant {TenantId}...",
                runId, tenantId);

            usedFallback = true;
            fallbackReason = failureReason;

            try
            {
                var fallbackRequest = new GroundedAnswerRequest(userQuery);
                finalResponse = await _groundedAnswerUseCase.GetGroundedAnswerAsync(fallbackRequest, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Plain-RAG fallback also failed for RunId={RunId}.", runId);
                finalResponse = new GroundedAnswerResponse(
                    GroundedAnswerStatus.Refused,
                    null,
                    $"Both orchestration and plain-RAG fallback failed: {ex.Message}",
                    Array.Empty<CitationItem>(),
                    "OrchestratorFallback",
                    "N/A",
                    TimeSpan.Zero);
            }
        }

        var status = orchestrationSuccess
            ? (finalResponse?.Status == GroundedAnswerStatus.Refused ? "Refused" : "Completed")
            : (cancellationToken.IsCancellationRequested
                ? "Cancelled"
                : (usedFallback ? "CompletedWithFallback" : "Failed"));

        return new OrchestrationRunRecord(
            RunId: runId,
            CorrelationId: resolvedCorrelationId,
            TenantId: tenantId,
            PatternName: PatternName,
            StartedAt: startedAt,
            CompletedAt: DateTimeOffset.UtcNow,
            Duration: totalStopwatch.Elapsed,
            Status: status,
            IterationCount: iterationCount,
            AgentExecutions: agentExecutions,
            UsedFallback: usedFallback,
            FallbackReason: fallbackReason,
            FinalResponse: finalResponse,
            PendingApproval: pendingApproval,
            FailureReason: orchestrationSuccess ? null : failureReason);
    }

    private async Task<AgentExecutionResult> ExecuteAgentWithRetryAsync(
        IAgent agent,
        AgentContext context,
        IReadOnlyDictionary<string, IAgentTool> allowedTools,
        CancellationToken cancellationToken)
    {
        int attempts = 0;
        int maxAttempts = 1 + Math.Max(0, _options.MaxRetries);
        int backoffMs = Math.Max(10, _options.RetryBackoffMilliseconds);

        while (true)
        {
            attempts++;
            try
            {
                var result = await agent.ExecuteAsync(context, allowedTools, cancellationToken);
                if (result.Success || attempts >= maxAttempts)
                {
                    return result;
                }

                _logger.LogWarning(
                    "Agent '{Role}' execution attempt {Attempt}/{MaxAttempts} failed. Backing off for {Backoff}ms...",
                    agent.Role, attempts, maxAttempts, backoffMs);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (attempts < maxAttempts && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    ex, "Agent '{Role}' thrown exception on attempt {Attempt}/{MaxAttempts}. Backing off for {Backoff}ms...",
                    agent.Role, attempts, maxAttempts, backoffMs);
            }
            catch (Exception ex)
            {
                return new AgentExecutionResult(
                    agent.Role,
                    Success: false,
                    Output: $"Agent '{agent.Role}' failed after {attempts} attempts.",
                    ToolCalls: Array.Empty<AgentToolCallRecord>(),
                    Duration: TimeSpan.Zero,
                    ErrorMessage: ex.Message);
            }

            if (attempts >= maxAttempts)
            {
                return new AgentExecutionResult(
                    agent.Role,
                    Success: false,
                    Output: $"Agent '{agent.Role}' failed after {attempts} attempts.",
                    ToolCalls: Array.Empty<AgentToolCallRecord>(),
                    Duration: TimeSpan.Zero,
                    ErrorMessage: $"Exhausted maximum retry attempts ({_options.MaxRetries}).");
            }

            await Task.Delay(backoffMs * attempts, cancellationToken);
        }
    }

    private static StreamProgressEvent BuildEvent(
        string runId,
        string correlationId,
        Guid tenantId,
        StreamEventType eventType,
        string stage,
        string status,
        double elapsedMs,
        string? agentRole = null,
        string? toolName = null,
        string? message = null,
        string? chunk = null,
        GroundedAnswerResponse? finalResponse = null,
        string? approvalRequestId = null,
        string? approvalAction = null,
        string? errorMessage = null)
    {
        return new StreamProgressEvent(
            RunId: runId,
            CorrelationId: correlationId,
            TenantId: tenantId,
            EventType: eventType,
            Timestamp: DateTimeOffset.UtcNow,
            Stage: stage,
            Status: status,
            ElapsedMs: elapsedMs,
            AgentRole: agentRole,
            ToolName: toolName,
            Message: message,
            Chunk: chunk,
            FinalResponse: finalResponse,
            ApprovalRequestId: approvalRequestId,
            ApprovalAction: approvalAction,
            ErrorMessage: errorMessage);
    }

    private static string? TruncateErrorMessage(string? msg, int maxLength = 500)
        => msg is null ? null : (msg.Length <= maxLength ? msg : msg.Substring(0, maxLength) + "...");
}
