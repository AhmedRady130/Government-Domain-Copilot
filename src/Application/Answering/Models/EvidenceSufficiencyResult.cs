namespace GovernmentDomainCopilot.Application.Answering.Models;

public sealed record EvidenceSufficiencyResult(
    bool IsSufficient,
    string? RefusalReason = null);
