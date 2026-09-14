using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GovernmentDomainCopilot.Application.Abstractions;
using GovernmentDomainCopilot.Application.Agents.Abstractions;
using GovernmentDomainCopilot.Application.Agents.Models;
using GovernmentDomainCopilot.Application.Agents.Services;
using GovernmentDomainCopilot.Application.Answering.Abstractions;
using GovernmentDomainCopilot.Application.Answering.Models;
using GovernmentDomainCopilot.Application.Sessions.Abstractions;
using GovernmentDomainCopilot.Application.Sessions.Services;
using GovernmentDomainCopilot.Application.Traces.Services;
using GovernmentDomainCopilot.Application.Traces.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Application.Tests.Sessions;

public sealed class SessionRunLinkingTests
{
    // --- Stubs ---

    private sealed class FakeTenantContext : ITenantContext
    {
        private readonly Guid _tenantId;
        public FakeTenantContext(Guid tenantId) => _tenantId = tenantId;
        public Guid GetTenantId() => _tenantId;
    }

    private sealed class FakeGroundedAnswerUseCase : IGroundedAnswerUseCase
    {
        private readonly GroundedAnswerStatus _status;
        public FakeGroundedAnswerUseCase(GroundedAnswerStatus status = GroundedAnswerStatus.Grounded)
            => _status = status;

        public Task<GroundedAnswerResponse> GetGroundedAnswerAsync(
            GroundedAnswerRequest request, CancellationToken cancellationToken)
        {
            var response = new GroundedAnswerResponse(
                _status,
                _status == GroundedAnswerStatus.Grounded ? "Policy answer [1]." : null,
                _status == GroundedAnswerStatus.Refused ? "Insufficient evidence." : null,
                new List<CitationItem> { new("1", Guid.NewGuid(), Guid.NewGuid(), "ref.pdf", "Policy", 1) },
                "FakeProvider",
                "FakeModel",
                TimeSpan.FromMilliseconds(10));
            return Task.FromResult(response);
        }
    }

    /// <summary>
    /// Minimal agent that sets a GroundedAnswerResponse on the context and signals TerminateEarly.
    /// </summary>
    private sealed class SuccessAgent : IAgent
    {
        private readonly IGroundedAnswerUseCase _useCase;
        public string Role => "Response Drafter Agent";
        public string Description => "Stub drafting agent for tests";
        public IReadOnlySet<string> AllowedToolNames => new HashSet<string>();
        public string InputContract => "UserQuery";
        public string OutputContract => "GroundedAnswer";
        public string TerminationCondition => "Always";

        public SuccessAgent(IGroundedAnswerUseCase useCase) => _useCase = useCase;

        public async Task<AgentExecutionResult> ExecuteAsync(
            AgentContext context,
            IReadOnlyDictionary<string, IAgentTool> availableTools,
            CancellationToken cancellationToken)
        {
            var response = await _useCase.GetGroundedAnswerAsync(
                new GroundedAnswerRequest(context.UserQuery),
                cancellationToken);

            context.SetState("GroundedAnswerResponse", response);

            return new AgentExecutionResult(
                AgentRole: Role,
                Success: true,
                Output: response.Answer ?? "No answer",
                ToolCalls: Array.Empty<AgentToolCallRecord>(),
                Duration: TimeSpan.FromMilliseconds(10),
                TerminateEarly: true);
        }
    }

    private static SequentialPipelineOrchestrator BuildOrchestrator(
        ITenantContext tenantContext,
        IGroundedAnswerUseCase groundedUseCase,
        IRunTraceStore? traceStore = null,
        ISessionStore? sessionStore = null)
    {
        var agents = new IAgent[] { new SuccessAgent(groundedUseCase) };
        var tools = Array.Empty<IAgentTool>();
        var options = Options.Create(new OrchestrationOptions
        {
            MaxIterations = 5,
            TimeoutSeconds = 30,
            MaxRetries = 0,
            RetryBackoffMilliseconds = 10,
            EnableFallback = false
        });

        return new SequentialPipelineOrchestrator(
            agents,
            tools,
            groundedUseCase,
            tenantContext,
            options,
            NullLogger<SequentialPipelineOrchestrator>.Instance,
            runTraceStore: traceStore,
            sessionStore: sessionStore);
    }

    // --- Tests ---

    [Fact]
    public async Task OrchestrateAsync_WithValidSessionId_RecordsTraceAndSessionMessages()
    {
        var tenantId = Guid.NewGuid();
        var tenantContext = new FakeTenantContext(tenantId);
        var groundedUseCase = new FakeGroundedAnswerUseCase(GroundedAnswerStatus.Grounded);
        var traceStore = new InMemoryRunTraceStore();
        var sessionStore = new InMemorySessionStore();

        var orchestrator = BuildOrchestrator(tenantContext, groundedUseCase, traceStore, sessionStore);

        var session = await sessionStore.CreateSessionAsync(tenantId, "Linking Test Session");

        var result = await orchestrator.OrchestrateAsync(
            "What is the policy?",
            correlationId: "corr-123",
            sessionId: session.SessionId,
            cancellationToken: CancellationToken.None);

        // Run should succeed
        Assert.True(result.Status is "Completed");
        Assert.Equal(session.SessionId, result.SessionId);

        // Trace should be recorded with session link
        var runTrace = await traceStore.GetRunAsync(result.RunId, tenantId);
        Assert.NotNull(runTrace);
        Assert.Equal(session.SessionId, runTrace.SessionId);
        Assert.Equal("corr-123", runTrace.CorrelationId);

        // Session should have 2 messages: user + assistant
        var messages = await sessionStore.GetMessagesAsync(session.SessionId, tenantId);
        Assert.NotNull(messages);
        Assert.Equal(2, messages!.Count);

        Assert.Equal("user", messages[0].Role);
        Assert.Equal("What is the policy?", messages[0].Content);

        Assert.Equal("assistant", messages[1].Role);
        Assert.Equal(result.RunId, messages[1].LinkedRunId);
        Assert.Equal("Completed", messages[1].Status);
    }

    [Fact]
    public async Task OrchestrateAsync_WithNonExistentSessionId_ThrowsKeyNotFoundException()
    {
        var tenantId = Guid.NewGuid();
        var tenantContext = new FakeTenantContext(tenantId);
        var groundedUseCase = new FakeGroundedAnswerUseCase();
        var sessionStore = new InMemorySessionStore();

        var orchestrator = BuildOrchestrator(tenantContext, groundedUseCase, sessionStore: sessionStore);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            orchestrator.OrchestrateAsync("query", null, "sess-doesnotexist"));
    }

    [Fact]
    public async Task OrchestrateAsync_WithCrossTenantSessionId_ThrowsKeyNotFoundException()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var tenantContextA = new FakeTenantContext(tenantA);
        var groundedUseCase = new FakeGroundedAnswerUseCase();
        var sessionStore = new InMemorySessionStore();

        // Create session for tenant B
        var sessionForB = await sessionStore.CreateSessionAsync(tenantB, "Tenant B Session");

        // Orchestrator runs as tenant A - should reject tenant B's session ID
        var orchestrator = BuildOrchestrator(tenantContextA, groundedUseCase, sessionStore: sessionStore);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            orchestrator.OrchestrateAsync("query", null, sessionForB.SessionId));
    }

    [Fact]
    public async Task OrchestrateAsync_WithoutSessionId_DoesNotAppendToAnySession()
    {
        var tenantId = Guid.NewGuid();
        var tenantContext = new FakeTenantContext(tenantId);
        var groundedUseCase = new FakeGroundedAnswerUseCase();
        var traceStore = new InMemoryRunTraceStore();
        var sessionStore = new InMemorySessionStore();

        var orchestrator = BuildOrchestrator(tenantContext, groundedUseCase, traceStore, sessionStore);

        var result = await orchestrator.OrchestrateAsync("What is the policy?");

        // Trace should be recorded but not linked to any session
        var runTrace = await traceStore.GetRunAsync(result.RunId, tenantId);
        Assert.NotNull(runTrace);
        Assert.Null(runTrace.SessionId);

        // No sessions should have any messages
        var sessions = await sessionStore.ListSessionsAsync(tenantId);
        Assert.Empty(sessions);
    }
}

