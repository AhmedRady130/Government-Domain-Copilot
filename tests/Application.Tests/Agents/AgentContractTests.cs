using GovernmentDomainCopilot.Application.Abstractions;
using GovernmentDomainCopilot.Application.Agents.Abstractions;
using GovernmentDomainCopilot.Application.Agents.Implementations;
using GovernmentDomainCopilot.Application.Agents.Models;
using GovernmentDomainCopilot.Application.Answering.Abstractions;
using GovernmentDomainCopilot.Application.Answering.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using System.Text.Json;
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LookupAgents_DoNotEmitRawQueriesOrEvidenceInLogsOrDiagnostics(bool eligibility)
    {
        const string sensitiveQuery = "Applicant SSN 123-45-6789 and api-key=not-a-real-secret";
        const string sensitiveEvidence = "Retrieved evidence containing account number 987654321";
        var logger = new CollectingLogger();
        IAgent agent = eligibility
            ? new EligibilityIdentifierAgent(logger)
            : new ProcedureResolverAgent(logger);
        var toolName = eligibility ? "eligibility_lookup" : "procedure_lookup";
        var tool = new FakeTool(toolName, isSideEffecting: false, outputJson: sensitiveEvidence);
        var context = new AgentContext(Guid.NewGuid(), "run-1", "corr-1", sensitiveQuery);

        var result = await agent.ExecuteAsync(
            context,
            new Dictionary<string, IAgentTool> { [toolName] = tool },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.DoesNotContain(sensitiveQuery, logger.Messages);
        Assert.DoesNotContain(sensitiveEvidence, logger.Messages);
        Assert.DoesNotContain(sensitiveQuery, result.Output);
        Assert.DoesNotContain(sensitiveEvidence, result.Output);
        var call = Assert.Single(result.ToolCalls);
        Assert.DoesNotContain(sensitiveQuery, call.InputJson);
        Assert.DoesNotContain(sensitiveEvidence, call.OutputJson);
        Assert.Contains("Length", call.InputJson);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LookupAgent_ToolFailureContainingSensitiveData_IsSanitizedBeforeDiagnosticsCanPersist(bool eligibility)
    {
        const string sensitiveQuery = "Applicant SSN 123-45-6789";
        const string sensitiveCredential = "api-key=not-a-real-secret";
        var logger = new CollectingLogger();
        IAgent agent = eligibility
            ? new EligibilityIdentifierAgent(logger)
            : new ProcedureResolverAgent(logger);
        var toolName = eligibility ? "eligibility_lookup" : "procedure_lookup";
        var context = new AgentContext(Guid.NewGuid(), "run-1", "corr-1", sensitiveQuery);

        var result = await agent.ExecuteAsync(
            context,
            new Dictionary<string, IAgentTool> { [toolName] = new ThrowingTool(toolName, sensitiveCredential) },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ToolFailureCodes.ExecutionFailed, result.ErrorMessage);
        var call = Assert.Single(result.ToolCalls);
        Assert.Equal(ToolFailureCodes.ExecutionFailed, call.ErrorMessage);

        var persistedDiagnostics = JsonSerializer.Serialize(result);
        Assert.DoesNotContain(sensitiveQuery, persistedDiagnostics);
        Assert.DoesNotContain(sensitiveCredential, persistedDiagnostics);
        Assert.DoesNotContain(sensitiveQuery, logger.Messages);
        Assert.DoesNotContain(sensitiveCredential, logger.Messages);
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

        private readonly string _outputJson;

        public FakeTool(string name, bool isSideEffecting, string outputJson = "{}")
        {
            Name = name;
            IsSideEffecting = isSideEffecting;
            _outputJson = outputJson;
        }

        public Task<ToolExecutionResult> ExecuteAsync(AgentContext context, string inputJson, CancellationToken cancellationToken)
        {
            ExecutionCount++;
            return Task.FromResult(new ToolExecutionResult(true, _outputJson));
        }
    }

    private sealed class CollectingLogger : ILogger<EligibilityIdentifierAgent>, ILogger<ProcedureResolverAgent>
    {
        private readonly List<string> _messages = new();
        public string Messages => string.Join("\n", _messages);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => _messages.Add(formatter(state, exception));
    }

    private sealed class ThrowingTool : IAgentTool
    {
        private readonly string _sensitiveCredential;
        public string Name { get; }
        public string Description => "Test tool";
        public bool IsSideEffecting => false;

        public ThrowingTool(string name, string sensitiveCredential)
        {
            Name = name;
            _sensitiveCredential = sensitiveCredential;
        }

        public Task<ToolExecutionResult> ExecuteAsync(AgentContext context, string inputJson, CancellationToken cancellationToken) =>
            throw new InvalidOperationException($"upstream failure for {context.UserQuery}; {_sensitiveCredential}");
    }
}
