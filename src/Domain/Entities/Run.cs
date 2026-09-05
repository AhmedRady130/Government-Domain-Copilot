namespace GovernmentDomainCopilot.Domain.Entities;

public sealed class Run : TenantOwnedEntity
{
    public Run(Guid id, Guid tenantId, Guid conversationSessionId, string status, DateTimeOffset createdAtUtc)
        : base(id, tenantId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(conversationSessionId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(status);

        ConversationSessionId = conversationSessionId;
        Status = status;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid ConversationSessionId { get; private set; }

    public string Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
}
