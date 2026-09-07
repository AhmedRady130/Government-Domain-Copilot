namespace GovernmentDomainCopilot.Application.Evaluation.Models;

using GovernmentDomainCopilot.Application.Answering.Models;

public sealed record EvaluationCaseResult(
    string CaseId,
    bool Passed,
    bool RetrievalHit,
    bool Grounded,
    bool RefusalCorrect,
    GroundedAnswerStatus ActualStatus,
    string? ActualAnswer,
    string? ActualReason,
    IReadOnlyList<CitationItem> ActualCitations,
    TimeSpan Duration,
    string? FailureReason = null);
