using GovernmentDomainCopilot.API.Models;
using GovernmentDomainCopilot.Application.Documents;
using GovernmentDomainCopilot.Application.Documents.Commands;
using GovernmentDomainCopilot.Application.Documents.Validation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace GovernmentDomainCopilot.API.Endpoints;

public static class DocumentEndpoints
{
    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/documents", async (
            IngestDocumentApiRequest? request,
            IIngestDocumentUseCase useCase,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("DocumentEndpoints");

            if (request == null)
            {
                return Results.BadRequest(new
                {
                    error = "Invalid request",
                    details = new[] { new { property = "request", message = "Request body cannot be empty." } }
                });
            }

            var command = new IngestDocumentCommand(
                request.Title ?? string.Empty,
                request.SourceReference ?? string.Empty,
                request.SourceText ?? string.Empty);

            try
            {
                var result = await useCase.IngestAsync(command, cancellationToken);

                var response = new IngestDocumentApiResponse(
                    result.DocumentId,
                    result.ChunkCount,
                    "Completed");

                return Results.Created($"/api/documents/{result.DocumentId}", response);
            }
            catch (IngestionValidationException ex)
            {
                logger.LogWarning("Ingestion validation failed with {ErrorCount} errors.", ex.Errors.Count);

                return Results.BadRequest(new
                {
                    error = "Validation failed",
                    details = ex.Errors.Select(e => new { property = e.PropertyName, message = e.Message })
                });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("tenant", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("Tenant context operation rejected: {Message}", ex.Message);

                return Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Forbidden",
                    detail: "Tenant access or context authorization failure.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error occurred during document ingestion.");

                return Results.Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "Internal Server Error",
                    detail: "An unexpected error occurred processing your document request.");
            }
        })
        .WithName("IngestDocument")
        .Produces<IngestDocumentApiResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status500InternalServerError);

        return endpoints;
    }
}
