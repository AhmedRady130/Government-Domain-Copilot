using GovernmentDomainCopilot.Application.Abstractions;
using GovernmentDomainCopilot.Application.Agents.Abstractions;
using GovernmentDomainCopilot.Application.Agents.Implementations;
using GovernmentDomainCopilot.Application.Agents.Models;
using GovernmentDomainCopilot.Application.Answering.Abstractions;
using GovernmentDomainCopilot.Application.Answering.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Application.Tests.Agents;

public sealed class AgentContractTests
{
    [Fact]
    public void EligibilityIdentifierAgent_Contract_IsExplicitAndValid()
    {
        var agent = new EligibilityIdentifierAgent(NullLogger<EligibilityIdentifierAgent>.Instance);

        Assert.Equal("Eligibility Identifier Agent", agent.Role);
        Assert.False(string.IsNullOrWhiteSpace(agent.Description));
        Assert.Contains("eligibility_lookup", agent.AllowedToolNames);
        Assert.Contains("document_search", agent.AllowedToolNames);
        Assert.False(string.IsNullOrWhiteSpace(agent.InputContract));
        Assert.False(string.IsNullOrWhiteSpace(agent.OutputContract));
        Assert.False(string.IsNullOrWhiteSpace(agent.TerminationCondition));
    }

    [Fact]
    public void ProcedureResolverAgent_Contract_IsExplicitAndValid()
    {
        var agent = new ProcedureResolverAgent(NullLogger<ProcedureResolverAgent>.Instance);

        Assert.Equal("Procedure Resolver Agent", agent.Role);
        Assert.False(string.IsNullOrWhiteSpace(agent.Description));
        Assert.Contains("procedure_lookup", agent.AllowedToolNames);
        Assert.Contains("document_search", agent.AllowedToolNames);
        Assert.False(string.IsNullOrWhiteSpace(agent.InputContract));
        Assert.False(string.IsNullOrWhiteSpace(agent.OutputContract));
        Assert.False(string.IsNullOrWhiteSpace(agent.TerminationCondition));
    }

    [Fact]
    public void ResponseDrafterAgent_Contract_IsExplicitAndValid()
    {
        var groundedUseCase = new FakeGroundedAnswerUseCase(
            new GroundedAnswerResponse(GroundedAnswerStatus.Grounded, "Answer", null, Array.Empty<CitationItem>(), "P", "M", TimeSpan.Zero));
        var agent = new ResponseDrafterAgent(groundedUseCase, NullLogger<ResponseDrafterAgent>.Instance);

        Assert.Equal("Response Drafter Agent", agent.Role);
        Assert.False(string.IsNullOrWhiteSpace(agent.Description));
        Assert.Contains("create_draft_approval", agent.AllowedToolNames);
        Assert.DoesNotContain("document_search", agent.AllowedToolNames);
        Assert.False(string.IsNullOrWhiteSpace(agent.InputContract));
        Assert.False(string.IsNullOrWhiteSpace(agent.OutputContract));
        Assert.False(string.IsNullOrWhiteSpace(agent.TerminationCondition));
    }

    [Fact]
    public async Task ResponseDrafterAgent_WhenGroundedAnswerRefuses_TerminatesEarlyWithoutCallingApprovalTool()
    {
        var refusalResponse = new GroundedAnswerResponse(
            GroundedAnswerStatus.Refused,
            null,
            "Insufficient evidence to verify government requirements.",
            Array.Empty<CitationItem>(),
            "Gemini",
            "gemini-2.5-flash",
            TimeSpan.FromMilliseconds(50));

        var fakeUseCase = new FakeGroundedAnswerUseCase(refusalResponse);
        var agent = new ResponseDrafterAgent(fakeUseCase, NullLogger<ResponseDrafterAgent>.Instance);

        var fakeApprovalTool = new FakeTool("create_draft_approval", isSideEffecting: true);
        var tools = new Dictionary<string, IAgentTool>
        {
            ["create_draft_approval"] = fakeApprovalTool
        };

        var context = new AgentContext(Guid.NewGuid(), "run-1", "corr-1", "Query with no evidence");
        var result = await agent.ExecuteAsync(context, tools, CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.TerminateEarly);
        Assert.Contains("Refusal produced", result.Output);
        Assert.Equal(0, fakeApprovalTool.ExecutionCount);
    }

    private sealed class FakeGroundedAnswerUseCase : IGroundedAnswerUseCase
    {
        private readonly GroundedAnswerResponse _response;

        public FakeGroundedAnswerUseCase(GroundedAnswerResponse response)
        {
            _response = response;
        }

        public Task<GroundedAnswerResponse> GetGroundedAnswerAsync(GroundedAnswerRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_response);
        }
    }

    private sealed class FakeTool : IAgentTool
    {
        public string Name { get; }
        public string Description => "Fake tool for tests";
        public bool IsSideEffecting { get; }
        public int ExecutionCount { get; private set; }

        public FakeTool(string name, bool isSideEffecting)
        {
            Name = name;
            IsSideEffecting = isSideEffecting;
        }

        public Task<ToolExecutionResult> ExecuteAsync(AgentContext context, string inputJson, CancellationToken cancellationToken)
        {
            ExecutionCount++;
            return Task.FromResult(new ToolExecutionResult(true, "{}"));
        }
    }
}
