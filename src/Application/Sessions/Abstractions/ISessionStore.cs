namespace GovernmentDomainCopilot.Application.Sessions.Abstractions;

using GovernmentDomainCopilot.Application.Answering.Models;
using GovernmentDomainCopilot.Application.Sessions.Models;

/// <summary>
/// Framework-agnostic store for server-side conversation sessions and interaction history.
/// All queries and writes are strictly scoped to the authenticated server-side tenant.
/// </summary>
public interface ISessionStore
{
    /// <summary>
    /// Creates a new conversation session for the authenticated tenant.
    /// </summary>
    Task<ConversationSessionRecord> CreateSessionAsync(
        Guid tenantId,
        string? title = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a conversation session by its identifier within the tenant.
    /// Returns null if not found or if the session belongs to another tenant.
    /// </summary>
    Task<ConversationSessionRecord?> GetSessionAsync(
        string sessionId,
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists sessions belonging to the tenant with pagination, ordered by LastActivityAt descending.
    /// </summary>
    Task<IReadOnlyList<ConversationSessionRecord>> ListSessionsAsync(
        Guid tenantId,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends a new message interaction to the session history.
    /// Validates tenant ownership and enforces content size limits.
    /// </summary>
    Task<SessionMessageRecord> AppendMessageAsync(
        string sessionId,
        Guid tenantId,
        string role,
        string content,
        string? status = null,
        IReadOnlyList<CitationItem>? citations = null,
        string? linkedRunId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all messages in chronological order for the specified session within the tenant.
    /// Returns null if the session does not exist or belongs to another tenant.
    /// </summary>
    Task<IReadOnlyList<SessionMessageRecord>?> GetMessagesAsync(
        string sessionId,
        Guid tenantId,
        CancellationToken cancellationToken = default);
}
