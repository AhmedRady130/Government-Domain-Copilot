namespace GovernmentDomainCopilot.Application.Sessions.Services;

using System.Collections.Concurrent;
using GovernmentDomainCopilot.Application.Answering.Models;
using GovernmentDomainCopilot.Application.Sessions.Abstractions;
using GovernmentDomainCopilot.Application.Sessions.Models;
using Microsoft.Extensions.Options;

/// <summary>
/// Thread-safe in-memory implementation of <see cref="ISessionStore"/>.
/// Note: In-memory storage is non-durable and not suitable for multi-replica production deployments.
/// Ready for future EF Core / PostgreSQL persistence.
/// </summary>
public sealed class InMemorySessionStore : ISessionStore
{
    private readonly ConcurrentDictionary<string, ConversationSessionRecord> _sessions = new();
    private readonly ConcurrentDictionary<string, List<SessionMessageRecord>> _messagesBySession = new();
    private readonly SessionOptions _options;

    public InMemorySessionStore(IOptions<SessionOptions>? options = null)
    {
        _options = options?.Value ?? new SessionOptions();
    }

    public Task<ConversationSessionRecord> CreateSessionAsync(
        Guid tenantId,
        string? title = null,
        CancellationToken cancellationToken = default)
    {
        var sessionId = $"sess-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        var session = new ConversationSessionRecord(
            SessionId: sessionId,
            TenantId: tenantId,
            Title: string.IsNullOrWhiteSpace(title) ? "New Conversation" : title.Trim(),
            CreatedAt: now,
            LastActivityAt: now,
            Status: "Active");

        _sessions[sessionId] = session;
        _messagesBySession[sessionId] = new List<SessionMessageRecord>();

        return Task.FromResult(session);
    }

    public Task<ConversationSessionRecord?> GetSessionAsync(
        string sessionId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (_sessions.TryGetValue(sessionId, out var session) && session.TenantId == tenantId)
        {
            return Task.FromResult<ConversationSessionRecord?>(session);
        }

        return Task.FromResult<ConversationSessionRecord?>(null);
    }

    public Task<IReadOnlyList<ConversationSessionRecord>> ListSessionsAsync(
        Guid tenantId,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        if (skip < 0) skip = 0;
        if (take <= 0) take = 50;
        if (take > 100) take = 100;

        var results = _sessions.Values
            .Where(s => s.TenantId == tenantId)
            .OrderByDescending(s => s.LastActivityAt)
            .ThenBy(s => s.SessionId)
            .Skip(skip)
            .Take(take)
            .ToList();

        return Task.FromResult<IReadOnlyList<ConversationSessionRecord>>(results);
    }

    public Task<SessionMessageRecord> AppendMessageAsync(
        string sessionId,
        Guid tenantId,
        string role,
        string content,
        string? status = null,
        IReadOnlyList<CitationItem>? citations = null,
        string? linkedRunId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        ArgumentNullException.ThrowIfNull(content);

        if (!_sessions.TryGetValue(sessionId, out var session) || session.TenantId != tenantId)
        {
            throw new KeyNotFoundException($"Session '{sessionId}' was not found for tenant '{tenantId}'.");
        }

        if (content.Length > _options.MaxMessageContentLength)
        {
            throw new ArgumentException(
                $"Message content length ({content.Length}) exceeds the maximum allowed length of {_options.MaxMessageContentLength} characters.",
                nameof(content));
        }

        var messageId = $"msg-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;

        var message = new SessionMessageRecord(
            MessageId: messageId,
            SessionId: sessionId,
            TenantId: tenantId,
            Role: role,
            Content: content,
            Status: status ?? (role.Equals("user", StringComparison.OrdinalIgnoreCase) ? "UserQuery" : "Completed"),
            Citations: citations ?? Array.Empty<CitationItem>(),
            LinkedRunId: linkedRunId,
            Timestamp: now);

        var list = _messagesBySession.GetOrAdd(sessionId, _ => new List<SessionMessageRecord>());
        lock (list)
        {
            list.Add(message);
        }

        // Update last activity and title if first message
        var updatedTitle = session.Title;
        if (session.Title == "New Conversation" && role.Equals("user", StringComparison.OrdinalIgnoreCase))
        {
            updatedTitle = content.Length > 50 ? content.Substring(0, 47) + "..." : content;
        }

        _sessions[sessionId] = session with
        {
            LastActivityAt = now,
            Title = updatedTitle
        };

        return Task.FromResult(message);
    }

    public Task<IReadOnlyList<SessionMessageRecord>?> GetMessagesAsync(
        string sessionId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (!_sessions.TryGetValue(sessionId, out var session) || session.TenantId != tenantId)
        {
            return Task.FromResult<IReadOnlyList<SessionMessageRecord>?>(null);
        }

        if (_messagesBySession.TryGetValue(sessionId, out var list))
        {
            lock (list)
            {
                return Task.FromResult<IReadOnlyList<SessionMessageRecord>?>(list.ToList());
            }
        }

        return Task.FromResult<IReadOnlyList<SessionMessageRecord>?>(new List<SessionMessageRecord>());
    }
}
