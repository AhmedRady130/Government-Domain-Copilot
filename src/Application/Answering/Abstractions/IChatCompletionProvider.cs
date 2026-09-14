namespace GovernmentDomainCopilot.Application.Answering.Abstractions;

using GovernmentDomainCopilot.Application.Answering.Models;

/// <summary>
/// Provider-independent completion abstraction for LLM interactions.
/// Designed so future streaming and tool-calling capabilities can be added cleanly.
/// </summary>
public interface IChatCompletionProvider
{
    /// <summary>
    /// Name of the completion provider (e.g. "Gemini").
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Generates a chat completion given a typed request.
    /// </summary>
    Task<ChatCompletionResult> CompleteAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Streams genuine incremental text chunks from the LLM provider.
    /// </summary>
    async IAsyncEnumerable<string> StreamCompleteAsync(
        ChatCompletionRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var result = await CompleteAsync(request, cancellationToken);
        if (!string.IsNullOrEmpty(result.Content))
        {
            yield return result.Content;
        }
    }
}
