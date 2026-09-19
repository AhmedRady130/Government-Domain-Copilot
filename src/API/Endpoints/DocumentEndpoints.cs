using GovernmentDomainCopilot.API.Models;
using GovernmentDomainCopilot.Application.Documents;
using GovernmentDomainCopilot.Application.Documents.Commands;
using GovernmentDomainCopilot.Application.Documents.Validation;
using GovernmentDomainCopilot.Application.Documents.Security;
using GovernmentDomainCopilot.Domain.Entities;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using GovernmentDomainCopilot.Application.Observability;

namespace GovernmentDomainCopilot.API.Endpoints;

public static class DocumentEndpoints
{
    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/documents", async (
            IngestDocumentApiRequest? request,
            IIngestDocumentUseCase useCase,
            IPiiRedactor piiRedactor,
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

            var piiResult = piiRedactor.Redact(request.SourceText);
            if (piiResult.HasPii)
            {
                logger.LogInformation(
                    "Document ingestion redacted PII. EmailCount={EmailCount} PhoneNumberCount={PhoneNumberCount} NationalIdCount={NationalIdCount}",
                    piiResult.EmailCount,
                    piiResult.PhoneNumberCount,
                    piiResult.NationalIdCount);
            }

            var command = new IngestDocumentCommand(
                request.Title ?? string.Empty,
                request.SourceReference ?? string.Empty,
                piiResult.RedactedText);

            try
            {
                var result = await useCase.IngestAsync(command, cancellationToken);

                if (result.Status == DocumentIngestionStatus.Completed)
                {
                    var response = new IngestDocumentApiResponse(
                        result.DocumentId,
                        result.ChunkCount,
                        "Completed");

                    return Results.Created($"/api/documents/{result.DocumentId}", response);
                }

                var failedResponse = new IngestDocumentApiResponse(
                    result.DocumentId,
                    0,
                    "Failed");

                return Results.UnprocessableEntity(failedResponse);
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
                logger.LogSafeFailure(ex, "TenantContextRejected", "DocumentIngestionEndpoint");

                return Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Forbidden",
                    detail: "Tenant access or context authorization failure.");
            }
            catch (Exception ex)
            {
                logger.LogSafeFailure(ex, "DocumentIngestionFailed", "DocumentIngestionEndpoint");

                return Results.Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "Internal Server Error",
                    detail: "An unexpected error occurred processing your document request.");
            }
        })
        .WithName("IngestDocument")
        .WithTags("Documents")
        .WithSummary("Ingest a document")
        .RequireAuthorization()
        .Produces<IngestDocumentApiResponse>(StatusCodes.Status201Created)
        .Produces<IngestDocumentApiResponse>(StatusCodes.Status422UnprocessableEntity)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status500InternalServerError);

        // GET /api/documents/{documentId}
        endpoints.MapGet("/api/documents/{documentId:guid}", async (
            Guid documentId,
            IDocumentRepository repository,
            GovernmentDomainCopilot.Application.Abstractions.ITenantContext tenantContext,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("DocumentEndpoints");
            var tenantId = tenantContext.GetTenantId();

            var doc = await repository.GetByIdAsync(tenantId, documentId, cancellationToken);
            if (doc == null)
            {
                return Results.NotFound(new
                {
                    error = "Not found",
                    details = $"Document '{documentId}' was not found for the authenticated tenant."
                });
            }

            var chunks = await repository.GetChunksByDocumentIdAsync(tenantId, documentId, cancellationToken);

            var response = new DocumentDetailApiResponse(
                doc.Id,
                doc.Title,
                doc.SourceReference,
                doc.IngestionStatus.ToString(),
                chunks.Count,
                doc.FailureReason,
                doc.CreatedAtUtc);

            return Results.Ok(response);
        })
        .WithName("GetDocument")
        .WithTags("Documents")
        .WithSummary("Get document details and ingestion status by ID")
        .WithDescription("Retrieves document metadata, ingestion status, and chunk count for the authenticated tenant.")
        .RequireAuthorization()
        .Produces<DocumentDetailApiResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
