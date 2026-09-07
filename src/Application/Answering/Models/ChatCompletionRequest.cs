namespace GovernmentDomainCopilot.Application.Answering.Models;

public sealed record ChatCompletionRequest(
    string SystemPrompt,
    string UserPrompt,
    string? Model = null,
    double? Temperature = null,
    int? MaxTokens = null);
