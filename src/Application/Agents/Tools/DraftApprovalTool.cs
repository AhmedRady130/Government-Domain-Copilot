namespace GovernmentDomainCopilot.Application.Agents.Tools;

using System.Text.Json;
using GovernmentDomainCopilot.Application.Agents.Abstractions;
using GovernmentDomainCopilot.Application.Agents.Models;

public sealed class DraftApprovalTool : IAgentTool
{
    private readonly IApprovalManager _approvalManager;

    public string Name => "create_draft_approval";
    public string Description => "Side-effecting tool: submits a finalized government response draft or official record for human supervisor approval. Consequential actions remain blocked until approved.";
    public bool IsSideEffecting => true;

    public DraftApprovalTool(IApprovalManager approvalManager)
    {
        _approvalManager = approvalManager ?? throw new ArgumentNullException(nameof(approvalManager));
    }

    public async Task<ToolExecutionResult> ExecuteAsync(
        AgentContext context,
        string inputJson,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        string proposedAction;
        string draftContent;

        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(inputJson) ? "{}" : inputJson);
            proposedAction = doc.RootElement.TryGetProperty("proposedAction", out var a)
                ? a.GetString() ?? "Publish Official Government Response"
                : "Publish Official Government Response";

            draftContent = doc.RootElement.TryGetProperty("draftContent", out var d)
                ? d.GetString() ?? string.Empty
                : string.Empty;
        }
        catch (JsonException ex)
        {
            return new ToolExecutionResult(false, "{}", $"Invalid JSON input: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(draftContent))
        {
            return new ToolExecutionResult(false, "{}", "Draft content cannot be empty.");
        }

        var request = await _approvalManager.CreateRequestAsync(
            context.TenantId,
            proposedAction,
            draftContent,
            cancellationToken);

        context.SetState("PendingApprovalRequest", request);

        var outputJson = JsonSerializer.Serialize(new
        {
            approvalRequestId = request.RequestId,
            tenantId = request.TenantId,
            status = request.Decision.ToString(),
            action = request.ProposedAction,
            message = "Draft queued for human supervisor review. No consequential side-effects executed."
        });

        return new ToolExecutionResult(
            Success: true,
            OutputJson: outputJson,
            RequiresApproval: true,
            ApprovalRequestId: request.RequestId);
    }
}
