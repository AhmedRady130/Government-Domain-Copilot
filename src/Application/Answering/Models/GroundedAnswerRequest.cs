namespace GovernmentDomainCopilot.Application.Answering.Models;

public sealed record GroundedAnswerRequest(
    string Query,
    int? TopK = null);
