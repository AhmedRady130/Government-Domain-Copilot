namespace GovernmentDomainCopilot.Application.Streaming.Services;

using System.Threading.Channels;
using GovernmentDomainCopilot.Application.Streaming.Abstractions;
using GovernmentDomainCopilot.Application.Streaming.Models;

/// <summary>
/// Channel-backed, thread-safe implementation of IOrchestrationEventSink.
/// Writes pipeline lifecycle events and genuine streaming answer chunks into a Channel.
/// </summary>
public sealed class ChannelBasedEventSink : IOrchestrationEventSink
{
    private readonly Channel<StreamProgressEvent> _channel;
    private readonly string _runId;
    private readonly string _correlationId;
    private readonly Guid _tenantId;

    public ChannelBasedEventSink(
        Channel<StreamProgressEvent> channel,
        string runId,
        string correlationId,
        Guid tenantId)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _runId = runId ?? throw new ArgumentNullException(nameof(runId));
        _correlationId = correlationId ?? throw new ArgumentNullException(nameof(correlationId));
        _tenantId = tenantId;
    }

    public void Emit(StreamProgressEvent progressEvent)
    {
        ArgumentNullException.ThrowIfNull(progressEvent);
        _channel.Writer.TryWrite(progressEvent);
    }

    public void EmitChunk(string chunk)
    {
        if (string.IsNullOrEmpty(chunk))
        {
            return;
        }

        _channel.Writer.TryWrite(new StreamProgressEvent(
            RunId: _runId,
            CorrelationId: _correlationId,
            TenantId: _tenantId,
            EventType: StreamEventType.AnswerChunk,
            Timestamp: DateTimeOffset.UtcNow,
            Stage: "ResponseDrafterAgent",
            Status: "Streaming",
            Chunk: chunk));
    }
}
