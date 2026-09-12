using GovernmentDomainCopilot.Application.Abstractions;
using GovernmentDomainCopilot.Application.Agents.Abstractions;
using GovernmentDomainCopilot.Application.Agents.Implementations;
using GovernmentDomainCopilot.Application.Agents.Models;
using GovernmentDomainCopilot.Application.Agents.Services;
using GovernmentDomainCopilot.Application.Agents.Tools;
using GovernmentDomainCopilot.Application.Answering.Abstractions;
using GovernmentDomainCopilot.Application.Answering.Models;
using GovernmentDomainCopilot.Application.Retrieval.Abstractions;
using GovernmentDomainCopilot.Application.Retrieval.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Application.Tests.Agents;

public sealed class SequentialPipelineOrchestratorTests
{
    private readonly Guid _tenantId = Guid.NewGuid();

    [Fact]
    public async Task OrchestrateAsync_SuccessfulPipeline_ExecutesAgentsInDeterministicSequence()
    {
        var tenantContext = new FakeTenantContext(_tenantId);
        var searchUseCase = new FakeHybridSearchUseCase();
        var approvalManager = new InMemoryApprovalManager();
        var groundedResponse = new GroundedAnswerResponse(
            GroundedAnswerStatus.Grounded,
            "Citizens must submit Form 104 and pay $50.",
            null,
            new List<CitationItem> { new("c1", Guid.NewGuid(), Guid.NewGuid(), "doc.pdf", "Permits", 1) },
            "Gemini",
            "gemini-2.5-flash",
            TimeSpan.FromMilliseconds(45));
        var groundedUseCase = new FakeGroundedAnswerUseCase(groundedResponse);

        var docTool = new DocumentSearchTool(searchUseCase);
        var eligTool = new EligibilityLookupTool(searchUseCase);
        var procTool = new ProcedureLookupTool(searchUseCase);
        var draftTool = new DraftApprovalTool(approvalManager);

        var tools = new IAgentTool[] { docTool, eligTool, procTool, draftTool };

        var eligAgent = new EligibilityIdentifierAgent(NullLogger<EligibilityIdentifierAgent>.Instance);
        var procAgent = new ProcedureResolverAgent(NullLogger<ProcedureResolverAgent>.Instance);
        var draftAgent = new ResponseDrafterAgent(groundedUseCase, NullLogger<ResponseDrafterAgent>.Instance);

        // Pass agents in reverse order to verify deterministic sorting
        var agents = new IAgent[] { draftAgent, eligAgent, procAgent };

        var options = Options.Create(new OrchestrationOptions
        {
            MaxIterations = 5,
            TimeoutSeconds = 10,
            MaxRetries = 2,
            RetryBackoffMilliseconds = 10,
            EnableFallback = true
        });

        var orchestrator = new SequentialPipelineOrchestrator(
            agents,
            tools,
            groundedUseCase,
            tenantContext,
            options,
            NullLogger<SequentialPipelineOrchestrator>.Instance);

        var result = await orchestrator.OrchestrateAsync("How do I apply for a building permit?");

        Assert.NotNull(result);
        Assert.Equal("Completed", result.Status);
        Assert.Equal(_tenantId, result.TenantId);
        Assert.False(result.UsedFallback);
        Assert.Equal(3, result.IterationCount);
        Assert.Equal(3, result.AgentExecutions.Count);

        // Verify execution sequence order
        Assert.Equal("Eligibility Identifier Agent", result.AgentExecutions[0].AgentRole);
        Assert.Equal("Procedure Resolver Agent", result.AgentExecutions[1].AgentRole);
        Assert.Equal("Response Drafter Agent", result.AgentExecutions[2].AgentRole);

        // Verify final answer & pending approval
        Assert.NotNull(result.FinalResponse);
        Assert.Equal("Citizens must submit Form 104 and pay $50.", result.FinalResponse.Answer);
        Assert.Single(result.FinalResponse.Citations);

        Assert.NotNull(result.PendingApproval);
        Assert.Equal(ApprovalDecision.Pending, result.PendingApproval.Decision);
        Assert.False(result.PendingApproval.IsExecuted);
    }

    [Fact]
    public async Task OrchestrateAsync_MaxIterationsExceeded_TerminatesAndFallsBackSafely()
    {
        var tenantContext = new FakeTenantContext(_tenantId);
        var searchUseCase = new FakeHybridSearchUseCase();
        var approvalManager = new InMemoryApprovalManager();
        var groundedUseCase = new FakeGroundedAnswerUseCase(new GroundedAnswerResponse(
            GroundedAnswerStatus.Grounded, "Fallback answer", null, Array.Empty<CitationItem>(), "P", "M", TimeSpan.Zero));

        var tools = new IAgentTool[]
        {
            new DocumentSearchTool(searchUseCase),
            new EligibilityLookupTool(searchUseCase),
            new ProcedureLookupTool(searchUseCase),
            new DraftApprovalTool(approvalManager)
        };

        var agents = new IAgent[]
        {
            new EligibilityIdentifierAgent(NullLogger<EligibilityIdentifierAgent>.Instance),
            new ProcedureResolverAgent(NullLogger<ProcedureResolverAgent>.Instance),
            new ResponseDrafterAgent(groundedUseCase, NullLogger<ResponseDrafterAgent>.Instance)
        };

        // Set MaxIterations = 1 (less than 3 agents required)
        var options = Options.Create(new OrchestrationOptions
        {
            MaxIterations = 1,
            TimeoutSeconds = 10,
            EnableFallback = true
        });

        var orchestrator = new SequentialPipelineOrchestrator(
            agents,
            tools,
            groundedUseCase,
            tenantContext,
            options,
            NullLogger<SequentialPipelineOrchestrator>.Instance);

        var result = await orchestrator.OrchestrateAsync("Query");

        Assert.True(result.UsedFallback);
        Assert.Equal("CompletedWithFallback", result.Status);
        Assert.Contains("Maximum iteration limit (1) exceeded", result.FallbackReason);
        Assert.NotNull(result.FinalResponse);
        Assert.Equal("Fallback answer", result.FinalResponse.Answer);
    }

    [Fact]
    public async Task OrchestrateAsync_MaxIterationsExceeded_WithoutFallback_ReturnsFailedStatus()
    {
        var tenantContext = new FakeTenantContext(_tenantId);
        var searchUseCase = new FakeHybridSearchUseCase();
        var approvalManager = new InMemoryApprovalManager();
        var groundedUseCase = new FakeGroundedAnswerUseCase(new GroundedAnswerResponse(
            GroundedAnswerStatus.Grounded, "Fallback answer", null, Array.Empty<CitationItem>(), "P", "M", TimeSpan.Zero));

        var tools = new IAgentTool[]
        {
            new DocumentSearchTool(searchUseCase),
            new EligibilityLookupTool(searchUseCase),
            new ProcedureLookupTool(searchUseCase),
            new DraftApprovalTool(approvalManager)
        };

        var agents = new IAgent[]
        {
            new EligibilityIdentifierAgent(NullLogger<EligibilityIdentifierAgent>.Instance),
            new ProcedureResolverAgent(NullLogger<ProcedureResolverAgent>.Instance),
            new ResponseDrafterAgent(groundedUseCase, NullLogger<ResponseDrafterAgent>.Instance)
        };

        var options = Options.Create(new OrchestrationOptions
        {
            MaxIterations = 1,
            EnableFallback = false
        });

        var orchestrator = new SequentialPipelineOrchestrator(
            agents,
            tools,
            groundedUseCase,
            tenantContext,
            options,
            NullLogger<SequentialPipelineOrchestrator>.Instance);

        var result = await orchestrator.OrchestrateAsync("Query");

        Assert.False(result.UsedFallback);
        Assert.Equal("Failed", result.Status);
        Assert.Contains("Maximum iteration limit (1) exceeded", result.FailureReason);
    }

    [Fact]
    public async Task OrchestrateAsync_CallerCancellation_CancelsImmediatelyAndNeverRunsFallback()
    {
        var tenantContext = new FakeTenantContext(_tenantId);
        var groundedUseCase = new FakeGroundedAnswerUseCase(new GroundedAnswerResponse(
            GroundedAnswerStatus.Grounded, "Fallback answer", null, Array.Empty<CitationItem>(), "P", "M", TimeSpan.Zero));

        var slowAgent = new CancellableSlowAgent("Eligibility Identifier Agent");
        var agents = new IAgent[] { slowAgent };
        var tools = Array.Empty<IAgentTool>();

        var options = Options.Create(new OrchestrationOptions
        {
            MaxIterations = 5,
            TimeoutSeconds = 30,
            EnableFallback = true // Fallback is enabled, but MUST NOT be called on cancellation!
        });

        var orchestrator = new SequentialPipelineOrchestrator(
            agents,
            tools,
            groundedUseCase,
            tenantContext,
            options,
            NullLogger<SequentialPipelineOrchestrator>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancel token

        var result = await orchestrator.OrchestrateAsync("Query", cancellationToken: cts.Token);

        Assert.Equal("Cancelled", result.Status);
        Assert.False(result.UsedFallback, "Explicit cancellation must never trigger Plain-RAG fallback!");
        Assert.Null(result.FinalResponse);
        Assert.Equal(0, groundedUseCase.CallCount);
    }

    [Fact]
    public async Task OrchestrateAsync_TransientFailure_RetriesAndSucceedsWithBackoff()
    {
        var tenantContext = new FakeTenantContext(_tenantId);
        var searchUseCase = new FakeHybridSearchUseCase();
        var approvalManager = new InMemoryApprovalManager();
        var groundedUseCase = new FakeGroundedAnswerUseCase(new GroundedAnswerResponse(
            GroundedAnswerStatus.Grounded, "Drafted", null, Array.Empty<CitationItem>(), "P", "M", TimeSpan.Zero));

        var flappyAgent = new FlappyAgent("Eligibility Identifier Agent", failCountBeforeSuccess: 1);
        var agents = new IAgent[] { flappyAgent };
        var tools = Array.Empty<IAgentTool>();

        var options = Options.Create(new OrchestrationOptions
        {
            MaxIterations = 5,
            TimeoutSeconds = 10,
            MaxRetries = 2,
            RetryBackoffMilliseconds = 10,
            EnableFallback = true
        });

        var orchestrator = new SequentialPipelineOrchestrator(
            agents,
            tools,
            groundedUseCase,
            tenantContext,
            options,
            NullLogger<SequentialPipelineOrchestrator>.Instance);

        var result = await orchestrator.OrchestrateAsync("Query");

        Assert.Equal("Completed", result.Status);
        Assert.Equal(2, flappyAgent.ExecutionAttempts);
        Assert.False(result.UsedFallback);
    }

    [Fact]
    public async Task OrchestrateAsync_RefusalStatus_TerminatesEarlyAndRecordsRefused()
    {
        var tenantContext = new FakeTenantContext(_tenantId);
        var searchUseCase = new FakeHybridSearchUseCase();
        var approvalManager = new InMemoryApprovalManager();
        var refusalResponse = new GroundedAnswerResponse(
            GroundedAnswerStatus.Refused,
            null,
            "Insufficient evidence available to support this inquiry.",
            Array.Empty<CitationItem>(),
            "Gemini",
            "gemini-2.5-flash",
            TimeSpan.FromMilliseconds(20));
        var groundedUseCase = new FakeGroundedAnswerUseCase(refusalResponse);

        var tools = new IAgentTool[]
        {
            new DocumentSearchTool(searchUseCase),
            new EligibilityLookupTool(searchUseCase),
            new ProcedureLookupTool(searchUseCase),
            new DraftApprovalTool(approvalManager)
        };

        var agents = new IAgent[]
        {
            new EligibilityIdentifierAgent(NullLogger<EligibilityIdentifierAgent>.Instance),
            new ProcedureResolverAgent(NullLogger<ProcedureResolverAgent>.Instance),
            new ResponseDrafterAgent(groundedUseCase, NullLogger<ResponseDrafterAgent>.Instance)
        };

        var options = Options.Create(new OrchestrationOptions
        {
            MaxIterations = 5,
            TimeoutSeconds = 10,
            EnableFallback = true
        });

        var orchestrator = new SequentialPipelineOrchestrator(
            agents,
            tools,
            groundedUseCase,
            tenantContext,
            options,
            NullLogger<SequentialPipelineOrchestrator>.Instance);

        var result = await orchestrator.OrchestrateAsync("Unanswerable question");

        Assert.Equal("Refused", result.Status);
        Assert.False(result.UsedFallback);
        Assert.Null(result.PendingApproval); // Refusals do not queue approval
    }

    private sealed class FakeTenantContext : ITenantContext
    {
        private readonly Guid _tenantId;
        public FakeTenantContext(Guid tenantId) => _tenantId = tenantId;
        public Guid GetTenantId() => _tenantId;
    }

    private sealed class FakeHybridSearchUseCase : IHybridSearchUseCase
    {
        public Task<HybridSearchResponse> SearchAsync(VectorSearchRequest request, CancellationToken cancellationToken)
        {
            var items = new List<RerankResultItem>
            {
                new(Guid.NewGuid(), Guid.NewGuid(), 0, "Title", "doc.pdf", "Eligible if resident for 6 months. Steps: 1. File form 104, fee $50.", 0.10, 0.90, 0.05, 1, 0.95, 1)
            };
            return Task.FromResult(new HybridSearchResponse(
                request.TopK ?? 5,
                items.Count,
                TimeSpan.FromMilliseconds(5),
                "HybridSearch",
                "WeightedSignalReranker",
                items));
        }
    }

    private sealed class FakeGroundedAnswerUseCase : IGroundedAnswerUseCase
    {
        private readonly GroundedAnswerResponse _response;
        public int CallCount { get; private set; }

        public FakeGroundedAnswerUseCase(GroundedAnswerResponse response)
        {
            _response = response;
        }

        public Task<GroundedAnswerResponse> GetGroundedAnswerAsync(GroundedAnswerRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_response);
        }
    }

    private sealed class CancellableSlowAgent : IAgent
    {
        public string Role { get; }
        public string Description => "Slow agent";
        public IReadOnlySet<string> AllowedToolNames => new HashSet<string>();
        public string InputContract => "Input";
        public string OutputContract => "Output";
        public string TerminationCondition => "End";

        public CancellableSlowAgent(string role) => Role = role;

        public async Task<AgentExecutionResult> ExecuteAsync(AgentContext context, IReadOnlyDictionary<string, IAgentTool> availableTools, CancellationToken cancellationToken)
        {
            await Task.Delay(5000, cancellationToken);
            return new AgentExecutionResult(Role, true, "Done", Array.Empty<AgentToolCallRecord>(), TimeSpan.Zero);
        }
    }

    private sealed class FlappyAgent : IAgent
    {
        public string Role { get; }
        public string Description => "Flappy agent";
        public IReadOnlySet<string> AllowedToolNames => new HashSet<string>();
        public string InputContract => "Input";
        public string OutputContract => "Output";
        public string TerminationCondition => "End";

        private readonly int _failCountBeforeSuccess;
        public int ExecutionAttempts { get; private set; }

        public FlappyAgent(string role, int failCountBeforeSuccess)
        {
            Role = role;
            _failCountBeforeSuccess = failCountBeforeSuccess;
        }

        public Task<AgentExecutionResult> ExecuteAsync(AgentContext context, IReadOnlyDictionary<string, IAgentTool> availableTools, CancellationToken cancellationToken)
        {
            ExecutionAttempts++;
            if (ExecutionAttempts <= _failCountBeforeSuccess)
            {
                return Task.FromResult(new AgentExecutionResult(Role, false, "Transient failure", Array.Empty<AgentToolCallRecord>(), TimeSpan.FromMilliseconds(5), ErrorMessage: "Transient error"));
            }

            return Task.FromResult(new AgentExecutionResult(Role, true, "Recovered successfully", Array.Empty<AgentToolCallRecord>(), TimeSpan.FromMilliseconds(5)));
        }
    }
}
