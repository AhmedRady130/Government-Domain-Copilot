namespace GovernmentDomainCopilot.Application.Answering.Models;

public sealed record CitationValidationResult(
    bool IsValid,
    string SanitizedAnswer,
    IReadOnlyList<CitationItem> ValidCitations,
    string? FailureReason = null);
