namespace GovernmentDomainCopilot.Application.Streaming.Abstractions;

using GovernmentDomainCopilot.Application.Streaming.Models;

/// <summary>
/// Channel-backed, thread-safe sink for streaming progress events and partial answer chunks.
/// Eliminates fragile mutable callbacks from domain and agent context state.
/// </summary>
public interface IOrchestrationEventSink
{
    /// <summary>
    /// Emits a structured pipeline lifecycle or progress event.
    /// </summary>
    void Emit(StreamProgressEvent progressEvent);

    /// <summary>
    /// Emits a genuine incremental token or text chunk streamed by the completion provider.
    /// </summary>
    void EmitChunk(string chunk);
}
