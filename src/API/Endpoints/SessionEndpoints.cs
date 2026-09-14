namespace GovernmentDomainCopilot.API.Endpoints;

using GovernmentDomainCopilot.API.Models;
using GovernmentDomainCopilot.Application.Abstractions;
using GovernmentDomainCopilot.Application.Agents.Abstractions;
using GovernmentDomainCopilot.Application.Answering.Abstractions;
using GovernmentDomainCopilot.Application.Answering.Models;
using GovernmentDomainCopilot.Application.Sessions.Abstractions;
using GovernmentDomainCopilot.Application.Sessions.Models;
using AppSessionOptions = GovernmentDomainCopilot.Application.Sessions.Models.SessionOptions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public static class SessionEndpoints
{
    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // 1. POST /api/sessions — Create Session
        endpoints.MapPost("/api/sessions", async (
            CreateSessionApiRequest? request,
            ISessionStore sessionStore,
            ITenantContext tenantContext,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("SessionEndpoints");
            var tenantId = tenantContext.GetTenantId();

            var session = await sessionStore.CreateSessionAsync(
                tenantId,
                request?.Title,
                cancellationToken);

            var response = new SessionApiResponse(
                session.SessionId,
                session.TenantId,
                session.Title,
                session.CreatedAt,
                session.LastActivityAt,
                session.Status);

            return Results.Created($"/api/sessions/{session.SessionId}", response);
        })
        .WithName("CreateSession")
        .WithTags("Sessions")
        .WithSummary("Create a new conversation session")
        .WithDescription("Creates a new server-side conversation session scoped strictly to the authenticated tenant.")
        .Produces<SessionApiResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest);

        // 2. GET /api/sessions — List Sessions
        endpoints.MapGet("/api/sessions", async (
            int? skip,
            int? take,
            ISessionStore sessionStore,
            ITenantContext tenantContext,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("SessionEndpoints");
            var tenantId = tenantContext.GetTenantId();

            var sessions = await sessionStore.ListSessionsAsync(
                tenantId,
                skip ?? 0,
                take ?? 50,
                cancellationToken);

            var response = sessions.Select(s => new SessionApiResponse(
                s.SessionId,
                s.TenantId,
                s.Title,
                s.CreatedAt,
                s.LastActivityAt,
                s.Status)).ToList();

            return Results.Ok(response);
        })
        .WithName("ListSessions")
        .WithTags("Sessions")
        .WithSummary("List conversation sessions for authenticated tenant")
        .WithDescription("Retrieves a paginated list of conversation sessions for the authenticated tenant ordered by last activity.")
        .Produces<IReadOnlyList<SessionApiResponse>>(StatusCodes.Status200OK);

        // 3. GET /api/sessions/{sessionId} — Get Session Detail
        endpoints.MapGet("/api/sessions/{sessionId}", async (
            string sessionId,
            ISessionStore sessionStore,
            ITenantContext tenantContext,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("SessionEndpoints");

            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return Results.BadRequest(new
                {
                    error = "Validation failed",
                    details = "SessionId cannot be empty."
                });
            }

            var tenantId = tenantContext.GetTenantId();
            var session = await sessionStore.GetSessionAsync(sessionId, tenantId, cancellationToken);

            if (session == null)
            {
                return Results.NotFound(new
                {
                    error = "Not found",
                    details = $"Session '{sessionId}' was not found for the authenticated tenant."
                });
            }

            var response = new SessionApiResponse(
                session.SessionId,
                session.TenantId,
                session.Title,
                session.CreatedAt,
                session.LastActivityAt,
                session.Status);

            return Results.Ok(response);
        })
        .WithName("GetSession")
        .WithTags("Sessions")
        .WithSummary("Get conversation session by ID")
        .WithDescription("Retrieves session metadata by ID if it exists and belongs to the authenticated tenant.")
        .Produces<SessionApiResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);

        // 4. GET /api/sessions/{sessionId}/messages — Get Session Messages
        endpoints.MapGet("/api/sessions/{sessionId}/messages", async (
            string sessionId,
            ISessionStore sessionStore,
            ITenantContext tenantContext,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("SessionEndpoints");

            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return Results.BadRequest(new
                {
                    error = "Validation failed",
                    details = "SessionId cannot be empty."
                });
            }

            var tenantId = tenantContext.GetTenantId();
            var messages = await sessionStore.GetMessagesAsync(sessionId, tenantId, cancellationToken);

            if (messages == null)
            {
                return Results.NotFound(new
                {
                    error = "Not found",
                    details = $"Session '{sessionId}' was not found for the authenticated tenant."
                });
            }

            var response = messages.Select(m => new SessionMessageApiResponse(
                m.MessageId,
                m.SessionId,
                m.Role,
                m.Content,
                m.Status,
                m.Citations.Select(c => new CitationItemApiResponse(
                    c.CitationId,
                    c.ChunkId,
                    c.DocumentId,
                    c.SourceReference,
                    c.Title,
                    c.Sequence)).ToList(),
                m.LinkedRunId,
                m.Timestamp)).ToList();

            return Results.Ok(response);
        })
        .WithName("GetSessionMessages")
        .WithTags("Sessions")
        .WithSummary("Get message history for a conversation session")
        .WithDescription("Retrieves the chronological list of messages in a session for the authenticated tenant.")
        .Produces<IReadOnlyList<SessionMessageApiResponse>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);

        // 5. POST /api/sessions/{sessionId}/messages — Post Message & Generate Answer
        endpoints.MapPost("/api/sessions/{sessionId}/messages", async (
            string sessionId,
            PostSessionMessageApiRequest? request,
            ISessionStore sessionStore,
            IGroundedAnswerUseCase groundedAnswerUseCase,
            IMultiAgentOrchestrator orchestrator,
            ITenantContext tenantContext,
            IOptions<AppSessionOptions> sessionOptions,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("SessionEndpoints");

            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return Results.BadRequest(new
                {
                    error = "Validation failed",
                    details = "SessionId cannot be empty."
                });
            }

            if (request == null || string.IsNullOrWhiteSpace(request.Content))
            {
                return Results.BadRequest(new
                {
                    error = "Validation failed",
                    details = "Content is required in request body and cannot be empty."
                });
            }

            var maxLen = sessionOptions.Value?.MaxMessageContentLength ?? AppSessionOptions.DefaultMaxMessageContentLength;
            if (request.Content.Length > maxLen)
            {
                return Results.BadRequest(new
                {
                    error = "Validation failed",
                    details = $"Message content length ({request.Content.Length}) exceeds maximum allowed length of {maxLen} characters."
                });
            }

            var tenantId = tenantContext.GetTenantId();
            var session = await sessionStore.GetSessionAsync(sessionId, tenantId, cancellationToken);
            if (session == null)
            {
                return Results.NotFound(new
                {
                    error = "Not found",
                    details = $"Session '{sessionId}' was not found for the authenticated tenant."
                });
            }

            var isOrchestrate = string.Equals(request.Mode, "orchestrate", StringComparison.OrdinalIgnoreCase);

            if (isOrchestrate)
            {
                // Reuses existing IMultiAgentOrchestrator — orchestrator automatically records user & assistant messages
                var runRecord = await orchestrator.OrchestrateAsync(
                    request.Content,
                    request.CorrelationId,
                    sessionId,
                    cancellationToken);

                var citations = runRecord.FinalResponse?.Citations.Select(c => new CitationItemApiResponse(
                    c.CitationId,
                    c.ChunkId,
                    c.DocumentId,
                    c.SourceReference,
                    c.Title,
                    c.Sequence)).ToList() ?? new List<CitationItemApiResponse>();

                var answerText = runRecord.FinalResponse?.Answer
                    ?? (runRecord.Status == "Refused" ? runRecord.FinalResponse?.Reason : runRecord.FailureReason)
                    ?? "Orchestration completed without an answer.";

                var response = new SessionMessageApiResponse(
                    $"msg-{Guid.NewGuid():N}",
                    sessionId,
                    "assistant",
                    answerText,
                    runRecord.Status,
                    citations,
                    runRecord.RunId,
                    DateTimeOffset.UtcNow);

                return Results.Ok(response);
            }
            else
            {
                // Reuses existing IGroundedAnswerUseCase
                // 1. Append user message
                await sessionStore.AppendMessageAsync(
                    sessionId,
                    tenantId,
                    "user",
                    request.Content,
                    "UserQuery",
                    null,
                    null,
                    cancellationToken);

                // 2. Invoke grounded answer use case
                var groundedRequest = new GroundedAnswerRequest(request.Content);
                var answerResult = await groundedAnswerUseCase.GetGroundedAnswerAsync(groundedRequest, cancellationToken);

                var answerText = answerResult.Answer ?? answerResult.Reason ?? "No response generated.";
                var citations = answerResult.Citations.Select(c => new CitationItemApiResponse(
                    c.CitationId,
                    c.ChunkId,
                    c.DocumentId,
                    c.SourceReference,
                    c.Title,
                    c.Sequence)).ToList();

                // 3. Append finalized assistant message
                var assistantMessage = await sessionStore.AppendMessageAsync(
                    sessionId,
                    tenantId,
                    "assistant",
                    answerText,
                    answerResult.Status.ToString(),
                    answerResult.Citations,
                    null,
                    cancellationToken);

                var response = new SessionMessageApiResponse(
                    assistantMessage.MessageId,
                    sessionId,
                    "assistant",
                    assistantMessage.Content,
                    assistantMessage.Status,
                    citations,
                    null,
                    assistantMessage.Timestamp);

                return Results.Ok(response);
            }
        })
        .WithName("PostSessionMessage")
        .WithTags("Sessions")
        .WithSummary("Post message to session and generate assistant response")
        .WithDescription("Submits a user message to an active conversation session, invokes the grounded answer or orchestration pipeline, and returns the assistant reply.")
        .Produces<SessionMessageApiResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
