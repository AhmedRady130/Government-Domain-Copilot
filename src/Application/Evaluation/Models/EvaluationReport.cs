namespace GovernmentDomainCopilot.Application.Evaluation.Models;

public sealed record EvaluationReport(
    int TotalCases,
    int PassedCases,
    int FailedCases,
    double RetrievalHitRate,
    double GroundednessScore,
    double RefusalCorrectness,
    TimeSpan ExecutionDuration,
    IReadOnlyList<EvaluationCaseResult> CaseResults,
    DateTimeOffset Timestamp);
