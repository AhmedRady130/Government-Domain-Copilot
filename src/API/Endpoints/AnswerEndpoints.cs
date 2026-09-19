using GovernmentDomainCopilot.API.Models;
using GovernmentDomainCopilot.Application.Answering.Abstractions;
using GovernmentDomainCopilot.Application.Answering.Models;
using GovernmentDomainCopilot.Application.Retrieval.Exceptions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GovernmentDomainCopilot.API.Security;
using GovernmentDomainCopilot.Application.Observability;

namespace GovernmentDomainCopilot.API.Endpoints;

public static class AnswerEndpoints
{
    public static IEndpointRouteBuilder MapAnswerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/answer", async (
            GroundedAnswerApiRequest? request,
            IGroundedAnswerUseCase useCase,
            GovernmentDomainCopilot.Application.Sessions.Abstractions.ISessionStore sessionStore,
            GovernmentDomainCopilot.Application.Abstractions.ITenantContext tenantContext,
            IOptions<ApiSecurityOptions> securityOptions,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("AnswerEndpoints");

            if (request == null || string.IsNullOrWhiteSpace(request.Query))
            {
                return Results.BadRequest(new
                {
                    error = "Validation failed",
                    details = "Query is required in request body and cannot be empty."
                });
            }

            var validationFailure = QueryRequestValidator.Validate(request.Query, securityOptions.Value);
            if (validationFailure is not null)
                return validationFailure;

            var tenantId = tenantContext.GetTenantId();

            if (!string.IsNullOrWhiteSpace(request.SessionId))
            {
                var session = await sessionStore.GetSessionAsync(request.SessionId, tenantId, cancellationToken);
                if (session == null)
                {
                    return Results.NotFound(new
                    {
                        error = "Not found",
                        details = $"Session '{request.SessionId}' was not found for the authenticated tenant."
                    });
                }
            }

            try
            {
                var appRequest = new GroundedAnswerRequest(request.Query, request.TopK);
                var result = await useCase.GetGroundedAnswerAsync(appRequest, cancellationToken);

                var citations = result.Citations.Select(c => new CitationItemApiResponse(
                    c.CitationId,
                    c.ChunkId,
                    c.DocumentId,
                    c.SourceReference,
                    c.Title,
                    c.Sequence)).ToList();

                if (!string.IsNullOrWhiteSpace(request.SessionId))
                {
                    try
                    {
                        await sessionStore.AppendMessageAsync(
                            request.SessionId,
                            tenantId,
                            "user",
                            request.Query,
                            "UserQuery",
                            null,
                            null,
                            cancellationToken);

                        var answerText = result.Answer ?? result.Reason ?? "No response generated.";
                        await sessionStore.AppendMessageAsync(
                            request.SessionId,
                            tenantId,
                            "assistant",
                            answerText,
                            result.Status.ToString(),
                            result.Citations,
                            null,
                            cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogSafeFailure(ex, "SessionPersistenceFailed", "AppendAnswerSessionMessage");
                    }
                }

                var response = new GroundedAnswerApiResponse(
                    result.Status.ToString(),
                    result.Answer,
                    result.Reason,
                    citations,
                    result.ProviderName,
                    result.ModelName,
                    result.Duration.TotalMilliseconds,
                    request.SessionId);

                return Results.Ok(response);
            }
            catch (VectorSearchValidationException ex)
            {
                logger.LogSafeFailure(ex, "GroundedAnswerValidationFailed", "GroundedAnswerEndpoint");

                return Results.BadRequest(new
                {
                    error = "ValidationFailed"
                });
            }
            catch (InvalidOperationException ex) when (IsTenantContextFailure(ex))
            {
                logger.LogSafeFailure(ex, "TenantContextRejected", "GroundedAnswerEndpoint");

                return Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Forbidden",
                    detail: "Tenant access or context authorization failure.");
            }
            catch (Exception ex)
            {
                logger.LogSafeFailure(ex, "GroundedAnswerFailed", "GroundedAnswerEndpoint");

                return Results.Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "Internal Server Error",
                    detail: "An unexpected error occurred processing your grounded answer request.");
            }
        })
        .WithName("GroundedAnswer")
        .WithTags("Answer")
        .WithSummary("Generate grounded government domain answer")
        .WithDescription("Produces an evidence-grounded answer with citations or typed refusal for the user query.")
        .RequireAuthorization()
        .RequireRateLimiting(ApiRateLimitPolicies.AiWorkload)
        .Produces<GroundedAnswerApiResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status500InternalServerError);

        return endpoints;
    }

    private static bool IsTenantContextFailure(InvalidOperationException exception) =>
        exception.Message.Contains("tenant", StringComparison.OrdinalIgnoreCase);
}
