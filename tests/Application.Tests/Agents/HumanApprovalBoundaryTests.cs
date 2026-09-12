using GovernmentDomainCopilot.Application.Agents.Abstractions;
using GovernmentDomainCopilot.Application.Agents.Models;
using GovernmentDomainCopilot.Application.Agents.Services;
using Xunit;

namespace Application.Tests.Agents;

public sealed class HumanApprovalBoundaryTests
{
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    [Fact]
    public async Task CreateRequest_InitializesWithPendingStatus()
    {
        var manager = new InMemoryApprovalManager();

        var request = await manager.CreateRequestAsync(
            _tenantA,
            "Publish Building Permit Advisory",
            "Permit conditions and fees draft content");

        Assert.NotNull(request.RequestId);
        Assert.Equal(_tenantA, request.TenantId);
        Assert.Equal(ApprovalDecision.Pending, request.Decision);
        Assert.Null(request.DecidedAt);
        Assert.False(request.IsExecuted);
        Assert.Null(request.ExecutedAt);
    }

    [Fact]
    public async Task SubmitDecision_Approve_TransitionsToApproved()
    {
        var manager = new InMemoryApprovalManager();
        var request = await manager.CreateRequestAsync(_tenantA, "Publish Advisory", "Content");

        var decided = await manager.SubmitDecisionAsync(
            request.RequestId,
            _tenantA,
            ApprovalDecision.Approved,
            comments: "Verified by municipal supervisor");

        Assert.Equal(ApprovalDecision.Approved, decided.Decision);
        Assert.Equal("Verified by municipal supervisor", decided.ReviewerComments);
        Assert.NotNull(decided.DecidedAt);
        Assert.False(decided.IsExecuted);
    }

    [Fact]
    public async Task SubmitDecision_Reject_TransitionsToRejected()
    {
        var manager = new InMemoryApprovalManager();
        var request = await manager.CreateRequestAsync(_tenantA, "Publish Advisory", "Content");

        var decided = await manager.SubmitDecisionAsync(
            request.RequestId,
            _tenantA,
            ApprovalDecision.Rejected,
            comments: "Statutory citations incomplete");

        Assert.Equal(ApprovalDecision.Rejected, decided.Decision);
        Assert.Equal("Statutory citations incomplete", decided.ReviewerComments);
        Assert.NotNull(decided.DecidedAt);
        Assert.False(decided.IsExecuted);
    }

    [Fact]
    public async Task SubmitDecision_Edit_TransitionsToEditedWithModifiedPayload()
    {
        var manager = new InMemoryApprovalManager();
        var request = await manager.CreateRequestAsync(_tenantA, "Publish Advisory", "Original text");

        var decided = await manager.SubmitDecisionAsync(
            request.RequestId,
            _tenantA,
            ApprovalDecision.Edited,
            comments: "Corrected fee schedule",
            editedPayload: "Corrected text: Fee is $50");

        Assert.Equal(ApprovalDecision.Edited, decided.Decision);
        Assert.Equal("Corrected text: Fee is $50", decided.EditedPayload);
        Assert.Equal("Corrected fee schedule", decided.ReviewerComments);
        Assert.NotNull(decided.DecidedAt);
    }

    [Fact]
    public async Task SubmitDecision_CannotDecideTwice()
    {
        var manager = new InMemoryApprovalManager();
        var request = await manager.CreateRequestAsync(_tenantA, "Publish Advisory", "Original text");

        await manager.SubmitDecisionAsync(request.RequestId, _tenantA, ApprovalDecision.Approved);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.SubmitDecisionAsync(request.RequestId, _tenantA, ApprovalDecision.Rejected));
    }

    [Fact]
    public async Task ConsequentialAction_IsBlocked_WhenRequestIsPending()
    {
        var manager = new InMemoryApprovalManager();
        var request = await manager.CreateRequestAsync(_tenantA, "Publish Advisory", "Content");

        // Attempting to execute consequential action while still Pending must be blocked!
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.ExecuteActionAsync(request.RequestId, _tenantA));

        Assert.Contains("Pending", ex.Message);
        Assert.Contains("Consequential side effects require explicit approval", ex.Message);
    }

    [Fact]
    public async Task ConsequentialAction_IsBlocked_WhenRequestIsRejected()
    {
        var manager = new InMemoryApprovalManager();
        var request = await manager.CreateRequestAsync(_tenantA, "Publish Advisory", "Content");
        await manager.SubmitDecisionAsync(request.RequestId, _tenantA, ApprovalDecision.Rejected, "Rejected by authority");

        // Attempting to execute consequential action when Rejected must be blocked!
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.ExecuteActionAsync(request.RequestId, _tenantA));

        Assert.Contains("Rejected", ex.Message);
    }

    [Fact]
    public async Task ConsequentialAction_Succeeds_WhenApproved()
    {
        var manager = new InMemoryApprovalManager();
        var request = await manager.CreateRequestAsync(_tenantA, "Publish Advisory", "Content");
        await manager.SubmitDecisionAsync(request.RequestId, _tenantA, ApprovalDecision.Approved, "Approved");

        var executionResult = await manager.ExecuteActionAsync(request.RequestId, _tenantA);

        Assert.True(executionResult.Success);
        Assert.NotNull(executionResult.ExecutedAt);
        Assert.True(request.IsExecuted);
        Assert.NotNull(request.ExecutedAt);
    }

    [Fact]
    public async Task ConsequentialAction_Succeeds_WhenEdited()
    {
        var manager = new InMemoryApprovalManager();
        var request = await manager.CreateRequestAsync(_tenantA, "Publish Advisory", "Original");
        await manager.SubmitDecisionAsync(request.RequestId, _tenantA, ApprovalDecision.Edited, "Fixed", "Edited");

        var executionResult = await manager.ExecuteActionAsync(request.RequestId, _tenantA);

        Assert.True(executionResult.Success);
        Assert.True(request.IsExecuted);
    }

    [Fact]
    public async Task ConsequentialAction_CannotBeExecutedTwice()
    {
        var manager = new InMemoryApprovalManager();
        var request = await manager.CreateRequestAsync(_tenantA, "Publish Advisory", "Content");
        await manager.SubmitDecisionAsync(request.RequestId, _tenantA, ApprovalDecision.Approved);

        await manager.ExecuteActionAsync(request.RequestId, _tenantA);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.ExecuteActionAsync(request.RequestId, _tenantA));
    }

    [Fact]
    public async Task ApprovalManager_EnforcesTenantIsolation()
    {
        var manager = new InMemoryApprovalManager();
        var request = await manager.CreateRequestAsync(_tenantA, "Tenant A Secret Action", "Payload A");

        // Cross-tenant get returns null
        var fetchedByTenantB = await manager.GetRequestAsync(request.RequestId, _tenantB);
        Assert.Null(fetchedByTenantB);

        // Cross-tenant decision submission throws KeyNotFoundException
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            manager.SubmitDecisionAsync(request.RequestId, _tenantB, ApprovalDecision.Approved));

        // Cross-tenant execution throws KeyNotFoundException
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            manager.ExecuteActionAsync(request.RequestId, _tenantB));
    }
}
