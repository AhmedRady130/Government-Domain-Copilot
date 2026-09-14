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

    public GroundedAnswerUseCase(
        ITenantContext tenantContext,
        IHybridSearchUseCase hybridSearchUseCase,
        IChatCompletionProvider completionProvider,
        IEvidenceSufficiencyPolicy sufficiencyPolicy,
        ICitationValidator citationValidator,
        ILogger<GroundedAnswerUseCase> logger)
    {
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _hybridSearchUseCase = hybridSearchUseCase ?? throw new ArgumentNullException(nameof(hybridSearchUseCase));
        _completionProvider = completionProvider ?? throw new ArgumentNullException(nameof(completionProvider));
        _sufficiencyPolicy = sufficiencyPolicy ?? throw new ArgumentNullException(nameof(sufficiencyPolicy));
        _citationValidator = citationValidator ?? throw new ArgumentNullException(nameof(citationValidator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

        var completionRequest = new ChatCompletionRequest(
            SystemPrompt: GroundedAnswerPrompts.SystemPromptV1,
            UserPrompt: userPrompt);

        // Step 4: Invoke completion provider
        ChatCompletionResult completionResult;
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

                completionResult = new ChatCompletionResult(
                    contentBuilder.ToString(),
                    _completionProvider.ProviderName,
                    completionRequest.Model ?? "default",
                    stopwatch.Elapsed);
            }
            else
            {
                completionResult = await _completionProvider.CompleteAsync(completionRequest, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Chat completion provider failure for tenant {TenantId}.", tenantId);
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
