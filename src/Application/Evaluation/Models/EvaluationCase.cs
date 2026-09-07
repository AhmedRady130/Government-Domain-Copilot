namespace GovernmentDomainCopilot.Application.Evaluation.Models;

public sealed record EvaluationCase(
    string Id,
    Guid TenantId,
    string Category,
    string Query,
    bool ExpectRefusal,
    IReadOnlyList<string> ExpectedSourceReferences,
    IReadOnlyList<string> ExpectedKeywords,
    bool IsAdversarial,
    string? AdversarialType = null,
    string? Description = null);
