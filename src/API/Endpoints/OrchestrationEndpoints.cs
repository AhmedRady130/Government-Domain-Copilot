namespace GovernmentDomainCopilot.API.Endpoints;

using GovernmentDomainCopilot.API.Models;
using GovernmentDomainCopilot.Application.Abstractions;
using GovernmentDomainCopilot.Application.Agents.Abstractions;
using GovernmentDomainCopilot.Application.Agents.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

public static class OrchestrationEndpoints
{
    public static IEndpointRouteBuilder MapOrchestrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // 1. Multi-Agent Orchestration Run
        endpoints.MapPost("/api/orchestrate", async (
            OrchestrationApiRequest? request,
            IMultiAgentOrchestrator orchestrator,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("OrchestrationEndpoints");

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
                var runRecord = await orchestrator.OrchestrateAsync(
                    request.Query,
                    request.CorrelationId,
                    cancellationToken);

                var citations = runRecord.FinalResponse?.Citations.Select(c => new CitationItemApiResponse(
                    c.CitationId,
                    c.ChunkId,
                    c.DocumentId,
                    c.SourceReference,
                    c.Title,
                    c.Sequence)).ToList() ?? new List<CitationItemApiResponse>();

                var agentExecutions = runRecord.AgentExecutions.Select(a => new AgentExecutionDto(
                    a.AgentRole,
                    a.StartedAt,
                    a.CompletedAt,
                    a.Duration.TotalMilliseconds,
                    a.Success,
                    a.OutputSummary,
                    a.ErrorMessage)).ToList();

                PendingApprovalDto? pendingApproval = null;
                if (runRecord.PendingApproval != null)
                {
                    var apr = runRecord.PendingApproval;
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

                var response = new OrchestrationApiResponse(
                    runRecord.RunId,
                    runRecord.CorrelationId,
                    runRecord.TenantId,
                    runRecord.PatternName,
                    runRecord.Status,
                    runRecord.IterationCount,
                    runRecord.Duration.TotalMilliseconds,
                    runRecord.UsedFallback,
                    runRecord.FallbackReason,
                    runRecord.FinalResponse?.Answer,
                    citations,
                    agentExecutions,
                    pendingApproval,
                    runRecord.FailureReason);

                return Results.Ok(response);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error occurred during multi-agent orchestration.");
                return Results.Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "Internal Server Error",
                    detail: "An unexpected error occurred during multi-agent orchestration.");
            }
        })
        .WithName("MultiAgentOrchestrate")
        .Produces<OrchestrationApiResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError);

        // 2. Retrieve Approval Request
        endpoints.MapGet("/api/approvals/{requestId}", async (
            string requestId,
            IApprovalManager approvalManager,
            ITenantContext tenantContext,
            CancellationToken cancellationToken) =>
        {
            var tenantId = tenantContext.GetTenantId();
            var apr = await approvalManager.GetRequestAsync(requestId, tenantId, cancellationToken);
            if (apr == null)
            {
                return Results.NotFound(new
                {
                    error = "Not found",
                    details = $"Approval request '{requestId}' was not found for the authenticated tenant."
                });
            }

            var dto = new PendingApprovalDto(
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

            return Results.Ok(dto);
        })
        .WithName("GetApprovalRequest")
        .Produces<PendingApprovalDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        // 3. Submit Approval Decision (Approve / Reject / Edit)
        endpoints.MapPost("/api/approvals/{requestId}/decide", async (
            string requestId,
            ApprovalDecisionApiRequest? request,
            IApprovalManager approvalManager,
            ITenantContext tenantContext,
            CancellationToken cancellationToken) =>
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Decision))
            {
                return Results.BadRequest(new
                {
                    error = "Validation failed",
                    details = "Decision is required (Approved, Rejected, Edited)."
                });
            }

            if (!Enum.TryParse<ApprovalDecision>(request.Decision, true, out var decision) || decision == ApprovalDecision.Pending)
            {
                return Results.BadRequest(new
                {
                    error = "Validation failed",
                    details = "Decision must be one of: Approved, Rejected, Edited."
                });
            }

            var tenantId = tenantContext.GetTenantId();

            try
            {
                var apr = await approvalManager.SubmitDecisionAsync(
                    requestId,
                    tenantId,
                    decision,
                    request.Comments,
                    request.EditedPayload,
                    cancellationToken);

                var dto = new PendingApprovalDto(
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

                return Results.Ok(dto);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new
                {
                    error = "Not found",
                    details = $"Approval request '{requestId}' was not found for the authenticated tenant."
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new
                {
                    error = "Invalid state transition",
                    details = ex.Message
                });
            }
        })
        .WithName("SubmitApprovalDecision")
        .Produces<PendingApprovalDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);

        // 4. Safe Consequential Action Execution (Blocked until Approved/Edited)
        endpoints.MapPost("/api/approvals/{requestId}/execute", async (
            string requestId,
            IApprovalManager approvalManager,
            ITenantContext tenantContext,
            CancellationToken cancellationToken) =>
        {
            var tenantId = tenantContext.GetTenantId();

            try
            {
                var result = await approvalManager.ExecuteActionAsync(requestId, tenantId, cancellationToken);
                var response = new ApprovalExecutionApiResponse(
                    result.RequestId,
                    result.Success,
                    result.Status,
                    result.Message,
                    result.ExecutedAt);

                return Results.Ok(response);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new
                {
                    error = "Not found",
                    details = $"Approval request '{requestId}' was not found for the authenticated tenant."
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new
                {
                    error = "Execution blocked",
                    details = ex.Message
                });
            }
        })
        .WithName("ExecuteApprovedAction")
        .Produces<ApprovalExecutionApiResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
