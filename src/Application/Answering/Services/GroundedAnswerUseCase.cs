namespace GovernmentDomainCopilot.Application.Answering.Services;

using System.Diagnostics;
using GovernmentDomainCopilot.Application.Abstractions;
using GovernmentDomainCopilot.Application.Answering.Abstractions;
using GovernmentDomainCopilot.Application.Answering.Models;
using GovernmentDomainCopilot.Application.Answering.Prompts;
using GovernmentDomainCopilot.Application.Retrieval;
using GovernmentDomainCopilot.Application.Retrieval.Abstractions;
using GovernmentDomainCopilot.Application.Retrieval.Exceptions;
using GovernmentDomainCopilot.Application.Retrieval.Models;
using GovernmentDomainCopilot.Application.Streaming.Abstractions;
using Microsoft.Extensions.Logging;

public sealed class GroundedAnswerUseCase : IGroundedAnswerUseCase
{
    private readonly ITenantContext _tenantContext;
    private readonly IHybridSearchUseCase _hybridSearchUseCase;
    private readonly IChatCompletionProvider _completionProvider;
    private readonly IEvidenceSufficiencyPolicy _sufficiencyPolicy;
    private readonly ICitationValidator _citationValidator;
    private readonly ILogger<GroundedAnswerUseCase> _logger;
    private readonly ICorrelationContext? _correlationContext;
    private readonly GovernmentDomainCopilot.Application.Observability.Abstractions.ILlmTraceStore? _llmTraceStore;
    private readonly GovernmentDomainCopilot.Application.Observability.Abstractions.ICostCalculator? _costCalculator;

    public GroundedAnswerUseCase(
        ITenantContext tenantContext,
        IHybridSearchUseCase hybridSearchUseCase,
        IChatCompletionProvider completionProvider,
        IEvidenceSufficiencyPolicy sufficiencyPolicy,
        ICitationValidator citationValidator,
        ILogger<GroundedAnswerUseCase> logger,
        ICorrelationContext? correlationContext = null,
        GovernmentDomainCopilot.Application.Observability.Abstractions.ILlmTraceStore? llmTraceStore = null,
        GovernmentDomainCopilot.Application.Observability.Abstractions.ICostCalculator? costCalculator = null)
    {
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _hybridSearchUseCase = hybridSearchUseCase ?? throw new ArgumentNullException(nameof(hybridSearchUseCase));
        _completionProvider = completionProvider ?? throw new ArgumentNullException(nameof(completionProvider));
        _sufficiencyPolicy = sufficiencyPolicy ?? throw new ArgumentNullException(nameof(sufficiencyPolicy));
        _citationValidator = citationValidator ?? throw new ArgumentNullException(nameof(citationValidator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _correlationContext = correlationContext;
        _llmTraceStore = llmTraceStore;
        _costCalculator = costCalculator;
    }

    public Task<GroundedAnswerResponse> GetGroundedAnswerAsync(
        GroundedAnswerRequest request,
        CancellationToken cancellationToken)
    {
        return GetGroundedAnswerAsync(request, eventSink: null, cancellationToken);
    }

    public async Task<GroundedAnswerResponse> GetGroundedAnswerAsync(
        GroundedAnswerRequest request,
        IOrchestrationEventSink? eventSink,
        CancellationToken cancellationToken)
    {
        if (request == null)
        {
            throw new VectorSearchValidationException("Grounded answer request cannot be null.");
        }

        if (string.IsNullOrWhiteSpace(request.Query))
        {
            throw new VectorSearchValidationException("Query cannot be empty or whitespace.");
        }

        var tenantId = _tenantContext.GetTenantId();
        var stopwatch = Stopwatch.StartNew();

        int topK = request.TopK ?? VectorSearchLimits.DefaultTopK;

        // Step 1: Execute hybrid search & reranking pipeline
        var searchRequest = new VectorSearchRequest(request.Query, topK);
        var searchResponse = await _hybridSearchUseCase.SearchAsync(searchRequest, cancellationToken);

        // Step 2: Evaluate evidence sufficiency (Deterministic pre-LLM check)
        var sufficiencyResult = _sufficiencyPolicy.Evaluate(searchResponse.Items);
        if (!sufficiencyResult.IsSufficient)
        {
            stopwatch.Stop();
            _logger.LogInformation(
                "Grounded answer refused for tenant {TenantId}: Reason={Reason}, DurationMs={DurationMs}",
                tenantId,
                sufficiencyResult.RefusalReason,
                stopwatch.ElapsedMilliseconds);

            return new GroundedAnswerResponse(
                GroundedAnswerStatus.Refused,
                Answer: null,
                Reason: sufficiencyResult.RefusalReason,
                Citations: Array.Empty<CitationItem>(),
                ProviderName: _completionProvider.ProviderName,
                ModelName: "N/A",
                Duration: stopwatch.Elapsed);
        }

        // Step 3: Build tenant-scoped bounded evidence context
        var (evidenceContext, availableCitations) = EvidenceContextBuilder.BuildContext(searchResponse.Items);

        string userPrompt = $"USER QUESTION:\n{request.Query}\n\n{evidenceContext}";

        var correlationId = request.CorrelationId
            ?? _correlationContext?.CorrelationId
            ?? $"corr-{Guid.NewGuid():N}";
        var runId = request.RunId;
        var operationType = eventSink != null ? "StreamChatCompletion" : "ChatCompletion";

        GovernmentDomainCopilot.Application.Answering.Models.ChatCompletionUsageMetadata? capturedUsage = null;
        var completionRequest = new ChatCompletionRequest(
            SystemPrompt: GroundedAnswerPrompts.SystemPromptV1,
            UserPrompt: userPrompt,
            CorrelationId: correlationId,
            RunId: runId,
            OperationType: operationType,
            OnUsageResolved: usage => capturedUsage = usage);

        // Step 4: Invoke completion provider with safe LLM invocation tracing
        ChatCompletionResult completionResult;
        var llmStopwatch = Stopwatch.StartNew();
        var llmStartedAt = DateTimeOffset.UtcNow;

        try
        {
            if (eventSink != null)
            {
                var contentBuilder = new System.Text.StringBuilder();
                await foreach (var chunk in _completionProvider.StreamCompleteAsync(completionRequest, cancellationToken))
                {
                    contentBuilder.Append(chunk);
                    eventSink.EmitChunk(chunk);
                }

                llmStopwatch.Stop();
                var llmCompletedAt = DateTimeOffset.UtcNow;

                completionResult = new ChatCompletionResult(
                    contentBuilder.ToString(),
                    _completionProvider.ProviderName,
                    completionRequest.Model ?? "default",
                    llmStopwatch.Elapsed,
                    capturedUsage);

                if (_llmTraceStore != null)
                {
                    decimal? cost = _costCalculator?.CalculateEstimatedCost(
                        completionResult.ModelName,
                        capturedUsage?.PromptTokens,
                        capturedUsage?.CompletionTokens);

                    var trace = new GovernmentDomainCopilot.Application.Observability.Models.LlmInvocationTrace(
                        Id: Guid.NewGuid(),
                        TenantId: tenantId,
                        CorrelationId: correlationId,
                        RunId: runId,
                        ProviderName: completionResult.ProviderName,
                        ModelName: completionResult.ModelName,
                        OperationType: operationType,
                        StartedAt: llmStartedAt,
                        CompletedAt: llmCompletedAt,
                        Duration: llmStopwatch.Elapsed,
                        IsSuccess: true,
                        PromptTokens: capturedUsage?.PromptTokens,
                        CompletionTokens: capturedUsage?.CompletionTokens,
                        TotalTokens: capturedUsage?.TotalTokens,
                        EstimatedCost: cost,
                        ErrorMessage: null);

                    await _llmTraceStore.RecordTraceAsync(trace, cancellationToken);
                }
            }
            else
            {
                completionResult = await _completionProvider.CompleteAsync(completionRequest, cancellationToken);
                llmStopwatch.Stop();
                var llmCompletedAt = DateTimeOffset.UtcNow;

                if (_llmTraceStore != null)
                {
                    var usage = completionResult.Usage ?? capturedUsage;
                    decimal? cost = _costCalculator?.CalculateEstimatedCost(
                        completionResult.ModelName,
                        usage?.PromptTokens,
                        usage?.CompletionTokens);

                    var trace = new GovernmentDomainCopilot.Application.Observability.Models.LlmInvocationTrace(
                        Id: Guid.NewGuid(),
                        TenantId: tenantId,
                        CorrelationId: correlationId,
                        RunId: runId,
                        ProviderName: completionResult.ProviderName,
                        ModelName: completionResult.ModelName,
                        OperationType: operationType,
                        StartedAt: llmStartedAt,
                        CompletedAt: llmCompletedAt,
                        Duration: llmStopwatch.Elapsed,
                        IsSuccess: true,
                        PromptTokens: usage?.PromptTokens,
                        CompletionTokens: usage?.CompletionTokens,
                        TotalTokens: usage?.TotalTokens,
                        EstimatedCost: cost,
                        ErrorMessage: null);

                    await _llmTraceStore.RecordTraceAsync(trace, cancellationToken);
                }
            }
        }
        catch (Exception)
        {
            llmStopwatch.Stop();
            var llmCompletedAt = DateTimeOffset.UtcNow;
            // Provider exceptions can contain prompts, retrieved content, or credentials.  Do not
            // attach the exception to structured logging or durable observability records.
            _logger.LogError("Chat completion provider failure for tenant {TenantId}. ErrorCode={ErrorCode}",
                tenantId,
                "LlmInvocationFailed");

            if (_llmTraceStore != null)
            {
                var failedTrace = new GovernmentDomainCopilot.Application.Observability.Models.LlmInvocationTrace(
                    Id: Guid.NewGuid(),
                    TenantId: tenantId,
                    CorrelationId: correlationId,
                    RunId: runId,
                    ProviderName: _completionProvider.ProviderName,
                    ModelName: completionRequest.Model ?? "default",
                    OperationType: operationType,
                    StartedAt: llmStartedAt,
                    CompletedAt: llmCompletedAt,
                    Duration: llmStopwatch.Elapsed,
                    IsSuccess: false,
                    PromptTokens: null,
                    CompletionTokens: null,
                    TotalTokens: null,
                    EstimatedCost: null,
                    ErrorMessage: "LlmInvocationFailed");

                try
                {
                    await _llmTraceStore.RecordTraceAsync(failedTrace, CancellationToken.None);
                }
                catch (Exception)
                {
                    _logger.LogWarning("Failed to persist error trace for failed LLM call.");
                }
            }

            stopwatch.Stop();
            throw;
        }

        // Step 5: Validate citations and structural grounding
        var validationResult = _citationValidator.Validate(completionResult.Content, availableCitations);

        stopwatch.Stop();

        if (!validationResult.IsValid)
        {
            _logger.LogInformation(
                "Grounded answer citation validation failed for tenant {TenantId}: Reason={Reason}, DurationMs={DurationMs}",
                tenantId,
                validationResult.FailureReason,
                stopwatch.ElapsedMilliseconds);

            return new GroundedAnswerResponse(
                GroundedAnswerStatus.Refused,
                Answer: null,
                Reason: validationResult.FailureReason ?? "Failed structural citation validation.",
                Citations: Array.Empty<CitationItem>(),
                ProviderName: completionResult.ProviderName,
                ModelName: completionResult.ModelName,
                Duration: stopwatch.Elapsed);
        }

        _logger.LogInformation(
            "Grounded answer successfully generated for tenant {TenantId}: CitationsCount={CitationsCount}, Provider={Provider}, Model={Model}, DurationMs={DurationMs}",
            tenantId,
            validationResult.ValidCitations.Count,
            completionResult.ProviderName,
            completionResult.ModelName,
            stopwatch.ElapsedMilliseconds);

        return new GroundedAnswerResponse(
            GroundedAnswerStatus.Grounded,
            Answer: validationResult.SanitizedAnswer,
            Reason: null,
            Citations: validationResult.ValidCitations,
            ProviderName: completionResult.ProviderName,
            ModelName: completionResult.ModelName,
            Duration: stopwatch.Elapsed);
    }

}
