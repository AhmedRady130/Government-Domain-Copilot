namespace GovernmentDomainCopilot.Application.Answering.Models;

public sealed record GroundedAnswerResponse(
    GroundedAnswerStatus Status,
    string? Answer,
    string? Reason,
    IReadOnlyList<CitationItem> Citations,
    string ProviderName,
    string ModelName,
    TimeSpan Duration);
