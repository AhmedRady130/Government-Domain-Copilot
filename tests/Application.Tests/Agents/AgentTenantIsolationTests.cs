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

public sealed class AgentTenantIsolationTests
{
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    [Fact]
    public async Task DocumentSearchTool_ResolvesTenantFromTenantContext_NotInputJson()
    {
        var tenantContext = new ControllableTenantContext(_tenantA);
        var mockSearch = new TenantAwareHybridSearchUseCase(tenantContext);
        var tool = new DocumentSearchTool(mockSearch);

        // Attacker in Tenant A attempts to supply Tenant B's ID in input JSON
        var inputJsonWithTamperedTenant = $"{{\"query\":\"building permit\",\"tenantId\":\"{_tenantB}\"}}";
        var context = new AgentContext(_tenantA, "run-1", "corr-1", "building permit");

        var result = await tool.ExecuteAsync(context, inputJsonWithTamperedTenant, CancellationToken.None);

        Assert.True(result.Success);
        // The search was executed strictly under Tenant A, ignoring the tampered Tenant B ID in JSON
        Assert.Equal(_tenantA, mockSearch.LastSearchedTenantId);
        Assert.NotEqual(_tenantB, mockSearch.LastSearchedTenantId);
    }

    [Fact]
    public async Task EligibilityLookupTool_ResolvesTenantFromTenantContext_NotInputJson()
    {
        var tenantContext = new ControllableTenantContext(_tenantA);
        var mockSearch = new TenantAwareHybridSearchUseCase(tenantContext);
        var tool = new EligibilityLookupTool(mockSearch);

        var inputJsonWithTamperedTenant = $"{{\"topic\":\"pension\",\"tenantId\":\"{_tenantB}\"}}";
        var context = new AgentContext(_tenantA, "run-1", "corr-1", "pension");

        var result = await tool.ExecuteAsync(context, inputJsonWithTamperedTenant, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(_tenantA, mockSearch.LastSearchedTenantId);
    }

    [Fact]
    public async Task ProcedureLookupTool_ResolvesTenantFromTenantContext_NotInputJson()
    {
        var tenantContext = new ControllableTenantContext(_tenantA);
        var mockSearch = new TenantAwareHybridSearchUseCase(tenantContext);
        var tool = new ProcedureLookupTool(mockSearch);

        var inputJsonWithTamperedTenant = $"{{\"procedure\":\"passport\",\"tenantId\":\"{_tenantB}\"}}";
        var context = new AgentContext(_tenantA, "run-1", "corr-1", "passport");

        var result = await tool.ExecuteAsync(context, inputJsonWithTamperedTenant, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(_tenantA, mockSearch.LastSearchedTenantId);
    }

    [Fact]
    public async Task Orchestrator_ScopesExecutionStrictlyToAuthenticatedTenant()
    {
        var tenantContext = new ControllableTenantContext(_tenantA);
        var mockSearch = new TenantAwareHybridSearchUseCase(tenantContext);
        var approvalManager = new InMemoryApprovalManager();
        var groundedUseCase = new TenantAwareGroundedAnswerUseCase(tenantContext);

        var tools = new IAgentTool[]
        {
            new DocumentSearchTool(mockSearch),
            new EligibilityLookupTool(mockSearch),
            new ProcedureLookupTool(mockSearch),
            new DraftApprovalTool(approvalManager)
        };

        var agents = new IAgent[]
        {
            new EligibilityIdentifierAgent(NullLogger<EligibilityIdentifierAgent>.Instance),
            new ProcedureResolverAgent(NullLogger<ProcedureResolverAgent>.Instance),
            new ResponseDrafterAgent(groundedUseCase, NullLogger<ResponseDrafterAgent>.Instance)
        };

        var orchestrator = new SequentialPipelineOrchestrator(
            agents,
            tools,
            groundedUseCase,
            tenantContext,
            Options.Create(new OrchestrationOptions()),
            NullLogger<SequentialPipelineOrchestrator>.Instance);

        var runRecord = await orchestrator.OrchestrateAsync("Query for Tenant A");

        // OrchestrationRunRecord has Tenant A
        Assert.Equal(_tenantA, runRecord.TenantId);
        Assert.Equal(_tenantA, mockSearch.LastSearchedTenantId);
        Assert.Equal(_tenantA, groundedUseCase.LastInvokedTenantId);
        Assert.NotNull(runRecord.PendingApproval);
        Assert.Equal(_tenantA, runRecord.PendingApproval.TenantId);

        // Switch to Tenant B and run again
        tenantContext.CurrentTenantId = _tenantB;
        var runRecordB = await orchestrator.OrchestrateAsync("Query for Tenant B");

        Assert.Equal(_tenantB, runRecordB.TenantId);
        Assert.Equal(_tenantB, mockSearch.LastSearchedTenantId);
        Assert.Equal(_tenantB, groundedUseCase.LastInvokedTenantId);
        Assert.NotNull(runRecordB.PendingApproval);
        Assert.Equal(_tenantB, runRecordB.PendingApproval.TenantId);
    }

    [Fact]
    public async Task PlainRagFallback_PreservesTenantBoundary()
    {
        var tenantContext = new ControllableTenantContext(_tenantA);
        var mockSearch = new TenantAwareHybridSearchUseCase(tenantContext);
        var groundedUseCase = new TenantAwareGroundedAnswerUseCase(tenantContext);

        // An agent that fails to trigger fallback
        var failingAgent = new FailingAgent("Eligibility Identifier Agent");
        var agents = new IAgent[] { failingAgent };
        var tools = Array.Empty<IAgentTool>();

        var orchestrator = new SequentialPipelineOrchestrator(
            agents,
            tools,
            groundedUseCase,
            tenantContext,
            Options.Create(new OrchestrationOptions { EnableFallback = true }),
            NullLogger<SequentialPipelineOrchestrator>.Instance);

        var runRecord = await orchestrator.OrchestrateAsync("Trigger fallback");

        Assert.True(runRecord.UsedFallback);
        Assert.Equal("CompletedWithFallback", runRecord.Status);
        Assert.Equal(_tenantA, runRecord.TenantId);
        Assert.Equal(_tenantA, groundedUseCase.LastInvokedTenantId);
    }

    private sealed class ControllableTenantContext : ITenantContext
    {
        public Guid CurrentTenantId { get; set; }
        public ControllableTenantContext(Guid tenantId) => CurrentTenantId = tenantId;
        public Guid GetTenantId() => CurrentTenantId;
    }

    private sealed class TenantAwareHybridSearchUseCase : IHybridSearchUseCase
    {
        private readonly ITenantContext _tenantContext;
        public Guid? LastSearchedTenantId { get; private set; }

        public TenantAwareHybridSearchUseCase(ITenantContext tenantContext) => _tenantContext = tenantContext;

        public Task<HybridSearchResponse> SearchAsync(VectorSearchRequest request, CancellationToken cancellationToken)
        {
            LastSearchedTenantId = _tenantContext.GetTenantId();
            var items = new List<RerankResultItem>
            {
                new(Guid.NewGuid(), Guid.NewGuid(), 0, "Permit Doc", "doc.pdf", "Eligibility requirements: resident.", 0.10, 0.90, 0.05, 1, 0.90, 1)
            };
            return Task.FromResult(new HybridSearchResponse(
                request.TopK ?? 5,
                items.Count,
                TimeSpan.Zero,
                "HybridSearch",
                "Reranker",
                items));
        }
    }

    private sealed class TenantAwareGroundedAnswerUseCase : IGroundedAnswerUseCase
    {
        private readonly ITenantContext _tenantContext;
        public Guid? LastInvokedTenantId { get; private set; }

        public TenantAwareGroundedAnswerUseCase(ITenantContext tenantContext) => _tenantContext = tenantContext;

        public Task<GroundedAnswerResponse> GetGroundedAnswerAsync(GroundedAnswerRequest request, CancellationToken cancellationToken)
        {
            LastInvokedTenantId = _tenantContext.GetTenantId();
            return Task.FromResult(new GroundedAnswerResponse(
                GroundedAnswerStatus.Grounded,
                "Tenant scoped answer",
                null,
                new List<CitationItem> { new("c1", Guid.NewGuid(), Guid.NewGuid(), "ref.pdf", "Title", 1) },
                "Gemini",
                "gemini-2.5-flash",
                TimeSpan.Zero));
        }
    }

    private sealed class FailingAgent : IAgent
    {
        public string Role { get; }
        public string Description => "Always failing agent";
        public IReadOnlySet<string> AllowedToolNames => new HashSet<string>();
        public string InputContract => "Input";
        public string OutputContract => "Output";
        public string TerminationCondition => "End";

        public FailingAgent(string role) => Role = role;

        public Task<AgentExecutionResult> ExecuteAsync(AgentContext context, IReadOnlyDictionary<string, IAgentTool> availableTools, CancellationToken cancellationToken)
        {
            return Task.FromResult(new AgentExecutionResult(Role, false, "Fatal failure", Array.Empty<AgentToolCallRecord>(), TimeSpan.Zero, ErrorMessage: "Unrecoverable failure"));
        }
    }
}
