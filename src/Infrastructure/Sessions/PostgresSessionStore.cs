using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GovernmentDomainCopilot.Application.Answering.Models;
using GovernmentDomainCopilot.Application.Sessions.Abstractions;
using GovernmentDomainCopilot.Application.Sessions.Models;
using GovernmentDomainCopilot.Infrastructure.Persistence;
using GovernmentDomainCopilot.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GovernmentDomainCopilot.Infrastructure.Sessions;

/// <summary>
/// PostgreSQL / EF Core backed durable implementation of <see cref="ISessionStore"/>.
/// Enforces server-side tenant scoping on every operation.
/// </summary>
public sealed class PostgresSessionStore : ISessionStore
{
    private readonly GovernmentDomainCopilotDbContext _dbContext;
    private readonly SessionOptions _options;

    public PostgresSessionStore(
        GovernmentDomainCopilotDbContext dbContext,
        IOptions<SessionOptions>? options = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _options = options?.Value ?? new SessionOptions();
    }

    public async Task<ConversationSessionRecord> CreateSessionAsync(
        Guid tenantId,
        string? title = null,
        CancellationToken cancellationToken = default)
    {
        var sessionId = $"sess-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        var finalTitle = string.IsNullOrWhiteSpace(title) ? "New Conversation" : title.Trim();

        var entity = new ConversationSessionEntity
        {
            SessionId = sessionId,
            TenantId = tenantId,
            Title = finalTitle,
            CreatedAt = now,
            LastActivityAt = now,
            Status = "Active"
        };

        _dbContext.UserSessions.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new ConversationSessionRecord(
            SessionId: entity.SessionId,
            TenantId: entity.TenantId,
            Title: entity.Title,
            CreatedAt: entity.CreatedAt,
            LastActivityAt: entity.LastActivityAt,
            Status: entity.Status);
    }

    public async Task<ConversationSessionRecord?> GetSessionAsync(
        string sessionId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var entity = await _dbContext.UserSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SessionId == sessionId && s.TenantId == tenantId, cancellationToken);

        if (entity == null)
        {
            return null;
        }

        return new ConversationSessionRecord(
            SessionId: entity.SessionId,
            TenantId: entity.TenantId,
            Title: entity.Title,
            CreatedAt: entity.CreatedAt,
            LastActivityAt: entity.LastActivityAt,
            Status: entity.Status);
    }

    public async Task<IReadOnlyList<ConversationSessionRecord>> ListSessionsAsync(
        Guid tenantId,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        if (skip < 0) skip = 0;
        if (take <= 0) take = 50;
        if (take > 100) take = 100;

        var entities = await _dbContext.UserSessions
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .OrderByDescending(s => s.LastActivityAt)
            .ThenBy(s => s.SessionId)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return entities.Select(e => new ConversationSessionRecord(
            SessionId: e.SessionId,
            TenantId: e.TenantId,
            Title: e.Title,
            CreatedAt: e.CreatedAt,
            LastActivityAt: e.LastActivityAt,
            Status: e.Status)).ToList();
    }

    public async Task<SessionMessageRecord> AppendMessageAsync(
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

        var session = await _dbContext.UserSessions
            .FirstOrDefaultAsync(s => s.SessionId == sessionId && s.TenantId == tenantId, cancellationToken);

        if (session == null)
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
        var citationsList = citations ?? Array.Empty<CitationItem>();
        var citationsJson = citationsList.Count > 0
            ? JsonSerializer.Serialize(citationsList)
            : null;

        var messageEntity = new SessionMessageEntity
        {
            MessageId = messageId,
            SessionId = sessionId,
            TenantId = tenantId,
            Role = role,
            Content = content,
            Status = status ?? (role.Equals("user", StringComparison.OrdinalIgnoreCase) ? "UserQuery" : "Completed"),
            CitationsJson = citationsJson,
            LinkedRunId = linkedRunId,
            Timestamp = now
        };

        _dbContext.SessionMessages.Add(messageEntity);

        // Update session's LastActivityAt and auto-generate title if default
        session.LastActivityAt = now;
        if (session.Title == "New Conversation" && role.Equals("user", StringComparison.OrdinalIgnoreCase))
        {
            session.Title = content.Length > 50 ? content.Substring(0, 47) + "..." : content;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new SessionMessageRecord(
            MessageId: messageEntity.MessageId,
            SessionId: messageEntity.SessionId,
            TenantId: messageEntity.TenantId,
            Role: messageEntity.Role,
            Content: messageEntity.Content,
            Status: messageEntity.Status,
            Citations: citationsList,
            LinkedRunId: messageEntity.LinkedRunId,
            Timestamp: messageEntity.Timestamp);
    }

    public async Task<IReadOnlyList<SessionMessageRecord>?> GetMessagesAsync(
        string sessionId,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        // Verify session exists and belongs to tenant
        var sessionExists = await _dbContext.UserSessions
            .AsNoTracking()
            .AnyAsync(s => s.SessionId == sessionId && s.TenantId == tenantId, cancellationToken);

        if (!sessionExists)
        {
            return null;
        }

        var messageEntities = await _dbContext.SessionMessages
            .AsNoTracking()
            .Where(m => m.SessionId == sessionId && m.TenantId == tenantId)
            .OrderBy(m => m.Timestamp)
            .ToListAsync(cancellationToken);

        var result = new List<SessionMessageRecord>(messageEntities.Count);
        foreach (var m in messageEntities)
        {
            IReadOnlyList<CitationItem> citations = Array.Empty<CitationItem>();
            if (!string.IsNullOrWhiteSpace(m.CitationsJson))
            {
                try
                {
                    citations = JsonSerializer.Deserialize<List<CitationItem>>(m.CitationsJson) ?? (IReadOnlyList<CitationItem>)Array.Empty<CitationItem>();
                }
                catch
                {
                    // Fallback to empty if deserialization fails
                }
            }

            result.Add(new SessionMessageRecord(
                MessageId: m.MessageId,
                SessionId: m.SessionId,
                TenantId: m.TenantId,
                Role: m.Role,
                Content: m.Content,
                Status: m.Status,
                Citations: citations,
                LinkedRunId: m.LinkedRunId,
                Timestamp: m.Timestamp));
        }

        return result;
    }
}
