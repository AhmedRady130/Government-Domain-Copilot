using GovernmentDomainCopilot.API.Models;
using GovernmentDomainCopilot.Application.Retrieval.Abstractions;
using GovernmentDomainCopilot.Application.Retrieval.Exceptions;
using GovernmentDomainCopilot.Application.Retrieval.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace GovernmentDomainCopilot.API.Endpoints;

public static class SearchEndpoints
{
    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/search", async (
            string? query,
            int? topK,
            IHybridSearchUseCase useCase,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("SearchEndpoints");

            if (string.IsNullOrWhiteSpace(query))
            {
                return Results.BadRequest(new
                {
                    error = "Validation failed",
                    details = "Query parameter is required and cannot be empty."
                });
            }

            var request = new VectorSearchRequest(query, topK);

            try
            {
                var result = await useCase.SearchAsync(request, cancellationToken);

                var items = result.Items.Select(item => new SearchResultItemApiResponse(
                    item.ChunkId,
                    item.DocumentId,
                    item.Sequence,
                    item.Title,
                    item.SourceReference,
                    item.Content,
                    item.Distance,
                    item.KeywordScore,
                    item.RrfScore,
                    item.Rank,
                    item.RerankScore,
                    item.FinalRank)).ToList();

                var response = new SearchApiResponse(
                    result.TopK,
                    result.TotalReturned,
                    result.Duration.TotalMilliseconds,
                    result.ProviderName,
                    result.ModelName,
                    items);

                return Results.Ok(response);
            }
            catch (VectorSearchValidationException ex)
            {
                logger.LogWarning("Search validation failed: {Message}", ex.Message);

                return Results.BadRequest(new
                {
                    error = "Validation failed",
                    details = ex.Message
                });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("tenant", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("Tenant context operation rejected during search: {Message}", ex.Message);

                return Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: "Forbidden",
                    detail: "Tenant access or context authorization failure.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error occurred during hybrid search.");

                return Results.Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "Internal Server Error",
                    detail: "An unexpected error occurred processing your search request.");
            }
        })
        .WithName("HybridSearch")
        .Produces<SearchApiResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status500InternalServerError);

        return endpoints;
    }
}
