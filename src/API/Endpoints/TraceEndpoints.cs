namespace GovernmentDomainCopilot.API.Endpoints;

using GovernmentDomainCopilot.API.Models;
using GovernmentDomainCopilot.Application.Abstractions;
using GovernmentDomainCopilot.Application.Observability.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

public static class TraceEndpoints
{
    public static IEndpointRouteBuilder MapTraceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // GET /api/traces/llm?correlationId=&runId=
        // Returns LLM invocation traces scoped strictly to the authenticated tenant.
        endpoints.MapGet("/api/traces/llm", async (
            string? correlationId,
            string? runId,
            ILlmTraceStore llmTraceStore,
            ITenantContext tenantContext,
            CancellationToken cancellationToken) =>
        {
            var tenantId = tenantContext.GetTenantId();

            IReadOnlyList<GovernmentDomainCopilot.Application.Observability.Models.LlmInvocationTrace> traces;

            if (!string.IsNullOrWhiteSpace(correlationId))
            {
                traces = await llmTraceStore.GetTracesByCorrelationIdAsync(correlationId, tenantId, cancellationToken);
            }
            else if (!string.IsNullOrWhiteSpace(runId))
            {
                traces = await llmTraceStore.GetTracesByRunIdAsync(runId, tenantId, cancellationToken);
            }
            else
            {
                return Results.BadRequest(new
                {
                    error = "Validation failed",
                    details = "At least one of 'correlationId' or 'runId' query parameters must be provided."
                });
            }

            var response = traces.Select(t => new LlmTraceApiResponse(
                t.Id,
                t.TenantId,
                t.CorrelationId,
                t.RunId,
                t.ProviderName,
                t.ModelName,
                t.OperationType,
                t.StartedAt,
                t.CompletedAt,
                t.Duration.TotalMilliseconds,
                t.IsSuccess,
                t.PromptTokens,
                t.CompletionTokens,
                t.TotalTokens,
                t.EstimatedCost,
                t.ErrorMessage)).ToList();

            return Results.Ok(response);
        })
        .WithName("ListLlmTraces")
        .WithTags("Observability")
        .WithSummary("List LLM invocation traces by correlationId or runId")
        .WithDescription(
            "Returns LLM invocation traces scoped to the authenticated tenant. " +
            "Requires either 'correlationId' or 'runId' query parameter. " +
            "Traces are always scoped to the server-side authenticated tenant identity.")
        .RequireAuthorization()
        .Produces<IReadOnlyList<LlmTraceApiResponse>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized);

        return endpoints;
    }
}
