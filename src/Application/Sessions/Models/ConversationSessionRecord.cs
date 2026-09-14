namespace GovernmentDomainCopilot.Application.Sessions.Models;

public sealed record ConversationSessionRecord(
    string SessionId,
    Guid TenantId,
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActivityAt,
    string Status = "Active");
