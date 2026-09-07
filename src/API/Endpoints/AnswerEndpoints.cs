using GovernmentDomainCopilot.API.Models;
using GovernmentDomainCopilot.Application.Answering.Abstractions;
using GovernmentDomainCopilot.Application.Answering.Models;
using GovernmentDomainCopilot.Application.Retrieval.Exceptions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace GovernmentDomainCopilot.API.Endpoints;

public static class AnswerEndpoints
{
    public static IEndpointRouteBuilder MapAnswerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/answer", async (
            GroundedAnswerApiRequest? request,
            IGroundedAnswerUseCase useCase,
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

                var response = new GroundedAnswerApiResponse(
                    result.Status.ToString(),
                    result.Answer,
                    result.Reason,
                    citations,
                    result.ProviderName,
                    result.ModelName,
                    result.Duration.TotalMilliseconds);

                return Results.Ok(response);
            }
            catch (VectorSearchValidationException ex)
            {
                logger.LogWarning("Grounded answer validation failed: {Message}", ex.Message);

                return Results.BadRequest(new
                {
                    error = "Validation failed",
                    details = ex.Message
                });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("tenant", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("Tenant context operation rejected during grounded answer: {Message}", ex.Message);

                return Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Forbidden",
                    detail: "Tenant access or context authorization failure.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error occurred during grounded answer generation.");

                return Results.Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "Internal Server Error",
                    detail: "An unexpected error occurred processing your grounded answer request.");
            }
        })
        .WithName("GroundedAnswer")
        .Produces<GroundedAnswerApiResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status500InternalServerError);

        return endpoints;
    }
}
