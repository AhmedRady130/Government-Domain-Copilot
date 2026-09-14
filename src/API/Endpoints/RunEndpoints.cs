namespace GovernmentDomainCopilot.API.Endpoints;

using GovernmentDomainCopilot.API.Models;
using GovernmentDomainCopilot.Application.Abstractions;
using GovernmentDomainCopilot.Application.Traces.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

public static class RunEndpoints
{
    public static IEndpointRouteBuilder MapRunEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // GET /api/runs
        endpoints.MapGet("/api/runs", async (
            int? skip,
            int? take,
            string? sessionId,
            IRunTraceStore runTraceStore,
            ITenantContext tenantContext,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("RunEndpoints");
            var tenantId = tenantContext.GetTenantId();

            var runs = await runTraceStore.ListRunsAsync(
                tenantId,
                skip ?? 0,
                take ?? 50,
                sessionId,
                cancellationToken);

            var response = runs.Select(r => new RunSummaryApiResponse(
                r.RunId,
                r.CorrelationId,
                r.TenantId,
                r.PatternName,
                r.Status,
                r.IterationCount,
                r.Duration.TotalMilliseconds,
                r.UsedFallback,
                r.FallbackReason,
                r.StartedAt,
                r.CompletedAt,
                r.SessionId)).ToList();

            return Results.Ok(response);
        })
        .WithName("ListRuns")
        .WithTags("Runs")
        .WithSummary("List orchestration runs for the authenticated tenant")
        .WithDescription("Retrieves a paginated list of orchestration run traces scoped strictly to the authenticated server-side tenant.")
        .RequireAuthorization()
        .Produces<IReadOnlyList<RunSummaryApiResponse>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized);

        // GET /api/runs/{runId}
        endpoints.MapGet("/api/runs/{runId}", async (
            string runId,
            IRunTraceStore runTraceStore,
            ITenantContext tenantContext,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("RunEndpoints");

            if (string.IsNullOrWhiteSpace(runId))
            {
                return Results.BadRequest(new
                {
                    error = "Validation failed",
                    details = "RunId cannot be empty."
                });
            }

            var tenantId = tenantContext.GetTenantId();
            var run = await runTraceStore.GetRunAsync(runId, tenantId, cancellationToken);

            if (run == null)
            {
                return Results.NotFound(new
                {
                    error = "Not found",
                    details = $"Run '{runId}' was not found for the authenticated tenant."
                });
            }

            var citations = run.FinalResponse?.Citations.Select(c => new CitationItemApiResponse(
                c.CitationId,
                c.ChunkId,
                c.DocumentId,
                c.SourceReference,
                c.Title,
                c.Sequence)).ToList() ?? new List<CitationItemApiResponse>();

            var agentExecutions = run.AgentExecutions.Select(a => new AgentExecutionDto(
                a.AgentRole,
                a.StartedAt,
                a.CompletedAt,
                a.Duration.TotalMilliseconds,
                a.Success,
                a.OutputSummary,
                a.ErrorMessage)).ToList();

            PendingApprovalDto? pendingApproval = null;
            if (run.PendingApproval != null)
            {
                var apr = run.PendingApproval;
                pendingApproval = new PendingApprovalDto(
                    apr.RequestId,
                    apr.TenantId,
                    apr.ProposedAction,
                    apr.OriginalPayload,
                    apr.EditedPayload,
                    apr.Decision.ToString(),
                    apr.ReviewerComments,
                    apr.CreatedAt,
                    apr.DecidedAt,
                    apr.IsExecuted,
                    apr.ExecutedAt);
            }

            var response = new RunDetailApiResponse(
                run.RunId,
                run.CorrelationId,
                run.TenantId,
                run.PatternName,
                run.Status,
                run.IterationCount,
                run.Duration.TotalMilliseconds,
                run.UsedFallback,
                run.FallbackReason,
                run.StartedAt,
                run.CompletedAt,
                run.SessionId,
                run.FinalResponse?.Answer,
                citations,
                agentExecutions,
                pendingApproval,
                run.FailureReason);

            return Results.Ok(response);
        })
        .WithName("GetRun")
        .WithTags("Runs")
        .WithSummary("Get orchestration run details by RunId")
        .WithDescription("Retrieves full details for a single orchestration run trace if it exists and belongs to the authenticated tenant.")
        .RequireAuthorization()
        .Produces<RunDetailApiResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
