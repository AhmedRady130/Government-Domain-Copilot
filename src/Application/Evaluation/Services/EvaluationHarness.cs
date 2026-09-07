namespace GovernmentDomainCopilot.Application.Evaluation.Services;

using System.Diagnostics;
using GovernmentDomainCopilot.Application.Abstractions;
using GovernmentDomainCopilot.Application.Answering.Abstractions;
using GovernmentDomainCopilot.Application.Answering.Models;
using GovernmentDomainCopilot.Application.Evaluation.Abstractions;
using GovernmentDomainCopilot.Application.Evaluation.Models;
using Microsoft.Extensions.Logging;

public sealed class EvaluationHarness : IEvaluationHarness
{
    private readonly IGoldenDatasetLoader _datasetLoader;
    private readonly IGroundedAnswerUseCase _groundedAnswerUseCase;
    private readonly IEvaluationMetricCalculator _metricCalculator;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<EvaluationHarness> _logger;

    public EvaluationHarness(
        IGoldenDatasetLoader datasetLoader,
        IGroundedAnswerUseCase groundedAnswerUseCase,
        IEvaluationMetricCalculator metricCalculator,
        ITenantContext tenantContext,
        ILogger<EvaluationHarness> logger)
    {
        _datasetLoader = datasetLoader ?? throw new ArgumentNullException(nameof(datasetLoader));
        _groundedAnswerUseCase = groundedAnswerUseCase ?? throw new ArgumentNullException(nameof(groundedAnswerUseCase));
        _metricCalculator = metricCalculator ?? throw new ArgumentNullException(nameof(metricCalculator));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<EvaluationReport> RunAsync(
        IReadOnlyList<EvaluationCase>? inputCases = null,
        CancellationToken cancellationToken = default)
    {
        var cases = inputCases ?? _datasetLoader.LoadDefault();
        _logger.LogInformation("Starting evaluation harness run for {Count} cases.", cases.Count);

        var caseResults = new List<EvaluationCaseResult>(cases.Count);
        var totalStopwatch = Stopwatch.StartNew();

        foreach (var c in cases)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (_tenantContext is IEvaluationTenantContext evalTenant)
            {
                evalTenant.SetCurrentTenantId(c.TenantId);
            }

            var caseStopwatch = Stopwatch.StartNew();
            GroundedAnswerResponse actualResponse;

            try
            {
                var request = new GroundedAnswerRequest(c.Query);
                actualResponse = await _groundedAnswerUseCase.GetGroundedAnswerAsync(request, cancellationToken);
                caseStopwatch.Stop();

                var result = _metricCalculator.EvaluateCase(c, actualResponse, caseStopwatch.Elapsed);
                caseResults.Add(result);

                _logger.LogInformation(
                    "Evaluated case '{CaseId}' ({Category}): Passed={Passed}, Status={Status}, DurationMs={DurationMs}",
                    c.Id, c.Category, result.Passed, result.ActualStatus, caseStopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                caseStopwatch.Stop();
                _logger.LogWarning(ex, "Unexpected exception thrown while executing evaluation case '{CaseId}'.", c.Id);

                var failedResult = new EvaluationCaseResult(
                    CaseId: c.Id,
                    Passed: false,
                    RetrievalHit: false,
                    Grounded: false,
                    RefusalCorrect: false,
                    ActualStatus: GroundedAnswerStatus.Refused,
                    ActualAnswer: null,
                    ActualReason: $"Evaluation execution exception: {ex.Message}",
                    ActualCitations: Array.Empty<CitationItem>(),
                    Duration: caseStopwatch.Elapsed,
                    FailureReason: $"Unexpected exception: {ex.Message}");

                caseResults.Add(failedResult);

                _logger.LogInformation(
                    "Evaluated case '{CaseId}' ({Category}): Passed=False, Status=ExecutionException, DurationMs={DurationMs}",
                    c.Id, c.Category, caseStopwatch.ElapsedMilliseconds);
            }
        }

        totalStopwatch.Stop();
        var report = _metricCalculator.CalculateReport(caseResults, totalStopwatch.Elapsed);

        _logger.LogInformation(
            "Evaluation completed: Total={Total}, Passed={Passed}, Failed={Failed}, HitRate={HitRate:P1}, Groundedness={Groundedness:P1}, RefusalCorrectness={RefusalCorrectness:P1}, DurationMs={DurationMs}",
            report.TotalCases, report.PassedCases, report.FailedCases,
            report.RetrievalHitRate, report.GroundednessScore, report.RefusalCorrectness,
            totalStopwatch.ElapsedMilliseconds);

        return report;
    }
}
