namespace GovernmentDomainCopilot.Application.Streaming.Models;

using System.Text.Json.Serialization;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StreamEventType
{
    RunStarted,
    AgentStarted,
    AgentProgress,
    ToolStarted,
    ToolCompleted,
    AnswerChunk,
    ApprovalRequired,
    FallbackStarted,
    RunCompleted,
    RunFailed,
    RunCancelled
}
