using GovernmentDomainCopilot.Application.Agents.Abstractions;
using GovernmentDomainCopilot.Application.Agents.Implementations;
using GovernmentDomainCopilot.Application.Agents.Models;
using GovernmentDomainCopilot.Application.Agents.Tools;
using GovernmentDomainCopilot.Application.Answering.Abstractions;
using GovernmentDomainCopilot.Application.Answering.Models;
using GovernmentDomainCopilot.Application.Retrieval.Abstractions;
using GovernmentDomainCopilot.Application.Retrieval.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Application.Tests.Agents;

public sealed class ToolAllowListAndAuthorizationTests
{
    [Fact]
    public void ToolSideEffectClassification_IsStrictlyEnforced()
    {
        var fakeSearch = new FakeHybridSearchUseCase();
        var fakeApprovalManager = new FakeApprovalManager();

        var docSearch = new DocumentSearchTool(fakeSearch);
        var eligibilityLookup = new EligibilityLookupTool(fakeSearch);
        var procedureLookup = new ProcedureLookupTool(fakeSearch);
        var draftApproval = new DraftApprovalTool(fakeApprovalManager);

        Assert.False(docSearch.IsSideEffecting, "document_search must be read-only.");
        Assert.False(eligibilityLookup.IsSideEffecting, "eligibility_lookup must be read-only.");
        Assert.False(procedureLookup.IsSideEffecting, "procedure_lookup must be read-only.");
        Assert.True(draftApproval.IsSideEffecting, "create_draft_approval must be marked side-effecting.");
    }

    [Fact]
    public void AgentToolAllowLists_AreExplicitAndSeparated()
    {
        var eligibilityAgent = new EligibilityIdentifierAgent(NullLogger<EligibilityIdentifierAgent>.Instance);
        var procedureAgent = new ProcedureResolverAgent(NullLogger<ProcedureResolverAgent>.Instance);
        var drafterAgent = new ResponseDrafterAgent(new FakeGroundedAnswerUseCase(), NullLogger<ResponseDrafterAgent>.Instance);

        // Eligibility agent can only query eligibility & docs
        Assert.Contains("eligibility_lookup", eligibilityAgent.AllowedToolNames);
        Assert.Contains("document_search", eligibilityAgent.AllowedToolNames);
        Assert.DoesNotContain("create_draft_approval", eligibilityAgent.AllowedToolNames);
        Assert.DoesNotContain("procedure_lookup", eligibilityAgent.AllowedToolNames);

        // Procedure agent can only query procedures & docs
        Assert.Contains("procedure_lookup", procedureAgent.AllowedToolNames);
        Assert.Contains("document_search", procedureAgent.AllowedToolNames);
        Assert.DoesNotContain("create_draft_approval", procedureAgent.AllowedToolNames);
        Assert.DoesNotContain("eligibility_lookup", procedureAgent.AllowedToolNames);

        // Drafter agent can only invoke side-effecting draft approval
        Assert.Contains("create_draft_approval", drafterAgent.AllowedToolNames);
        Assert.DoesNotContain("document_search", drafterAgent.AllowedToolNames);
        Assert.DoesNotContain("procedure_lookup", drafterAgent.AllowedToolNames);
        Assert.DoesNotContain("eligibility_lookup", drafterAgent.AllowedToolNames);
    }

    [Fact]
    public async Task AgentExecution_Fails_WhenAllowedToolIsNotProvidedInAvailableTools()
    {
        var agent = new EligibilityIdentifierAgent(NullLogger<EligibilityIdentifierAgent>.Instance);
        var emptyTools = new Dictionary<string, IAgentTool>();
        var context = new AgentContext(Guid.NewGuid(), "run-1", "corr-1", "Query");

        var result = await agent.ExecuteAsync(context, emptyTools, CancellationToken.None);

        Assert.False(result.Success);
        Assert.True(result.TerminateEarly);
        Assert.Contains("not available", result.Output);
    }

    [Fact]
    public async Task AgentExecution_RejectsTool_WhenToolNameIsNotInAgentAllowList()
    {
        var agent = new EligibilityIdentifierAgent(NullLogger<EligibilityIdentifierAgent>.Instance);
        // Provide unauthorized tool with name "create_draft_approval" mapped under the key it asks for
        var rogueTool = new RogueTool("create_draft_approval");
        var tools = new Dictionary<string, IAgentTool>
        {
            ["eligibility_lookup"] = rogueTool // Map unauthorized tool name to expected key
        };
        var context = new AgentContext(Guid.NewGuid(), "run-1", "corr-1", "Query");

        var result = await agent.ExecuteAsync(context, tools, CancellationToken.None);

        Assert.False(result.Success);
        Assert.True(result.TerminateEarly);
        Assert.Contains("Required tool not found or unauthorized", result.ErrorMessage);
    }

    [Fact]
    public async Task DraftApprovalTool_Fails_WhenDraftContentIsEmpty()
    {
        var fakeApprovalManager = new FakeApprovalManager();
        var tool = new DraftApprovalTool(fakeApprovalManager);
        var context = new AgentContext(Guid.NewGuid(), "run-1", "corr-1", "Query");

        var result = await tool.ExecuteAsync(context, "{\"draftContent\":\"\"}", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Draft content cannot be empty", result.ErrorMessage);
    }

    [Fact]
    public async Task DraftApprovalTool_Fails_WhenInputJsonIsMalformed()
    {
        var fakeApprovalManager = new FakeApprovalManager();
        var tool = new DraftApprovalTool(fakeApprovalManager);
        var context = new AgentContext(Guid.NewGuid(), "run-1", "corr-1", "Query");

        var result = await tool.ExecuteAsync(context, "not-json", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Invalid JSON input", result.ErrorMessage);
    }

    private sealed class RogueTool : IAgentTool
    {
        public string Name { get; }
        public string Description => "Rogue unauthorized tool";
        public bool IsSideEffecting => true;

        public RogueTool(string name)
        {
            Name = name;
        }

        public Task<ToolExecutionResult> ExecuteAsync(AgentContext context, string inputJson, CancellationToken cancellationToken)
        {
            return Task.FromResult(new ToolExecutionResult(true, "{}"));
        }
    }

    private sealed class FakeHybridSearchUseCase : IHybridSearchUseCase
    {
        public Task<HybridSearchResponse> SearchAsync(VectorSearchRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HybridSearchResponse(
                request.TopK ?? 5,
                0,
                TimeSpan.Zero,
                "HybridSearch",
                "WeightedSignalReranker",
                new List<RerankResultItem>()));
        }
    }

    private sealed class FakeApprovalManager : IApprovalManager
    {
        public Task<ApprovalRequest> CreateRequestAsync(Guid tenantId, string proposedAction, string payload, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ApprovalRequest("req-1", tenantId, proposedAction, payload));
        }

        public Task<ApprovalRequest?> GetRequestAsync(string requestId, Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult<ApprovalRequest?>(null);

        public Task<ApprovalRequest> SubmitDecisionAsync(string requestId, Guid tenantId, ApprovalDecision decision, string? comments = null, string? editedPayload = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<ApprovalExecutionResult> ExecuteActionAsync(string requestId, Guid tenantId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class FakeGroundedAnswerUseCase : IGroundedAnswerUseCase
    {
        public Task<GroundedAnswerResponse> GetGroundedAnswerAsync(GroundedAnswerRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new GroundedAnswerResponse(GroundedAnswerStatus.Grounded, "Answer", null, Array.Empty<CitationItem>(), "P", "M", TimeSpan.Zero));
        }
    }
}
