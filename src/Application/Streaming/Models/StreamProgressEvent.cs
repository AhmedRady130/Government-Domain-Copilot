namespace GovernmentDomainCopilot.Application.Streaming.Models;

using GovernmentDomainCopilot.Application.Answering.Models;

public sealed record StreamProgressEvent(
    string RunId,
    string CorrelationId,
    Guid TenantId,
    StreamEventType EventType,
    DateTimeOffset Timestamp,
    string Stage,
    string Status,
    double? ElapsedMs = null,
    string? AgentRole = null,
    string? ToolName = null,
    string? Message = null,
    string? Chunk = null,
    GroundedAnswerResponse? FinalResponse = null,
    string? ApprovalRequestId = null,
    string? ApprovalAction = null,
    string? ErrorMessage = null);
