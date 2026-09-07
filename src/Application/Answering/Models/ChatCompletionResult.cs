namespace GovernmentDomainCopilot.Application.Answering.Models;

public sealed record ChatCompletionResult(
    string Content,
    string ProviderName,
    string ModelName,
    TimeSpan Duration,
    ChatCompletionUsageMetadata? Usage = null);

public sealed record ChatCompletionUsageMetadata(
    int? PromptTokens,
    int? CompletionTokens,
    int? TotalTokens);
