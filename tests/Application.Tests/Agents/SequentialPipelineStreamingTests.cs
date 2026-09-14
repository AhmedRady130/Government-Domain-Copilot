namespace Application.Tests.Agents;

using System.Diagnostics;
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
using GovernmentDomainCopilot.Application.Streaming.Abstractions;
using GovernmentDomainCopilot.Application.Streaming.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

public sealed class SequentialPipelineStreamingTests
{
    private readonly Guid _tenantId = Guid.NewGuid();

    [Fact]
    public async Task OrchestrateStreamAsync_SuccessfulPipeline_EmitsRunStarted_AgentStarted_AndRunCompleted_InDeterministicOrder()
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
        var agents = new IAgent[] { draftAgent, eligAgent, procAgent }; // Out of order to verify sorting

        var options = Options.Create(new OrchestrationOptions
        {
            MaxIterations = 5,
            TimeoutSeconds = 10,
            MaxRetries = 1,
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

        var events = new List<StreamProgressEvent>();
        await foreach (var evt in orchestrator.OrchestrateStreamAsync("How to apply for permit?"))
        {
            events.Add(evt);
        }

        Assert.NotEmpty(events);

        // Invariant: First event is RunStarted
        Assert.Equal(StreamEventType.RunStarted, events.First().EventType);
        Assert.Equal(_tenantId, events.First().TenantId);

        // Invariant: Last event is terminal (RunCompleted)
        var terminalEvent = events.Last();
        Assert.Equal(StreamEventType.RunCompleted, terminalEvent.EventType);
        Assert.NotNull(terminalEvent.FinalResponse);
        Assert.Equal(GroundedAnswerStatus.Grounded, terminalEvent.FinalResponse.Status);
        Assert.Equal("Citizens must submit Form 104 and pay $50.", terminalEvent.FinalResponse.Answer);

        // Verify deterministic agent start ordering
        var agentStartEvents = events.Where(e => e.EventType == StreamEventType.AgentStarted).ToList();
        Assert.Equal(3, agentStartEvents.Count);
        Assert.Equal("Eligibility Identifier Agent", agentStartEvents[0].AgentRole);
        Assert.Equal("Procedure Resolver Agent", agentStartEvents[1].AgentRole);
        Assert.Equal("Response Drafter Agent", agentStartEvents[2].AgentRole);

        // Verify ApprovalRequired event was emitted when draft approval tool ran
        var approvalEvent = events.FirstOrDefault(e => e.EventType == StreamEventType.ApprovalRequired);
        Assert.NotNull(approvalEvent);
        Assert.NotNull(approvalEvent.ApprovalRequestId);
    }

    [Fact]
    public async Task OrchestrateStreamAsync_AnswerChunk_RepresentsPartialContent_NotFinalValidatedAnswer()
    {
        var tenantContext = new FakeTenantContext(_tenantId);
        var searchUseCase = new FakeHybridSearchUseCase();
        var approvalManager = new InMemoryApprovalManager();

        var streamedChunks = new List<string> { "Citizens ", "must submit ", "Form 104 [DOC-1]." };
        var groundedResponse = new GroundedAnswerResponse(
            GroundedAnswerStatus.Grounded,
            "Citizens must submit Form 104 [DOC-1].",
            null,
            new List<CitationItem> { new("DOC-1", Guid.NewGuid(), Guid.NewGuid(), "law.pdf", "Statute", 1) },
            "Gemini",
            "gemini-2.5-flash",
            TimeSpan.FromMilliseconds(50));

        var streamingGroundedUseCase = new StreamingFakeGroundedAnswerUseCase(groundedResponse, streamedChunks);

        var docTool = new DocumentSearchTool(searchUseCase);
        var draftTool = new DraftApprovalTool(approvalManager);
        var tools = new IAgentTool[] { docTool, draftTool };

        var draftAgent = new ResponseDrafterAgent(streamingGroundedUseCase, NullLogger<ResponseDrafterAgent>.Instance);
        var agents = new IAgent[] { draftAgent };

        var options = Options.Create(new OrchestrationOptions { TimeoutSeconds = 10 });
        var orchestrator = new SequentialPipelineOrchestrator(
            agents, tools, streamingGroundedUseCase, tenantContext, options, NullLogger<SequentialPipelineOrchestrator>.Instance);

        var events = new List<StreamProgressEvent>();
        await foreach (var evt in orchestrator.OrchestrateStreamAsync("Apply?"))
        {
            events.Add(evt);
        }

        var chunkEvents = events.Where(e => e.EventType == StreamEventType.AnswerChunk).ToList();
        Assert.Equal(3, chunkEvents.Count);

        // Prove: Individual chunk is partial, not equal to the final validated answer
        Assert.Equal("Citizens ", chunkEvents[0].Chunk);
        Assert.NotEqual(groundedResponse.Answer, chunkEvents[0].Chunk);
        Assert.Null(chunkEvents[0].FinalResponse);

        // Prove: RunCompleted event contains the full validated response with citations
        var completedEvent = events.Single(e => e.EventType == StreamEventType.RunCompleted);
        Assert.NotNull(completedEvent.FinalResponse);
        Assert.Equal(groundedResponse.Answer, completedEvent.FinalResponse.Answer);
        Assert.Single(completedEvent.FinalResponse.Citations);
    }

    [Fact]
    public async Task OrchestrateStreamAsync_InvalidCitationResponse_EmitsRunFailed_NeverRunCompleted()
    {
        var tenantContext = new FakeTenantContext(_tenantId);
        var searchUseCase = new FakeHybridSearchUseCase();
        var approvalManager = new InMemoryApprovalManager();

        // Response where citation validation failed
        var invalidCitationResponse = new GroundedAnswerResponse(
            GroundedAnswerStatus.Refused,
            null,
            "Structural citation validation failed: referenced [INVALID-99] not found in evidence.",
            Array.Empty<CitationItem>(),
            "Gemini",
            "gemini-2.5-flash",
            TimeSpan.FromMilliseconds(50));

        var groundedUseCase = new FakeGroundedAnswerUseCase(invalidCitationResponse);
        var draftTool = new DraftApprovalTool(approvalManager);
        var draftAgent = new ResponseDrafterAgent(groundedUseCase, NullLogger<ResponseDrafterAgent>.Instance);

        var options = Options.Create(new OrchestrationOptions { TimeoutSeconds = 10, EnableFallback = false });
        var orchestrator = new SequentialPipelineOrchestrator(
            new IAgent[] { draftAgent },
            new IAgentTool[] { draftTool },
            groundedUseCase,
            tenantContext,
            options,
            NullLogger<SequentialPipelineOrchestrator>.Instance);

        var events = new List<StreamProgressEvent>();
        await foreach (var evt in orchestrator.OrchestrateStreamAsync("Query"))
        {
            events.Add(evt);
        }

        // Invariant: Invalid citation response must NEVER emit RunCompleted
        Assert.DoesNotContain(events, e => e.EventType == StreamEventType.RunCompleted);

        // Terminal event must be RunFailed
        var terminal = events.Last();
        Assert.Equal(StreamEventType.RunFailed, terminal.EventType);
        Assert.Contains("citation validation failed", terminal.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OrchestrateStreamAsync_RefusalResponse_EmitsRunFailed_NeverRunCompleted()
    {
        var tenantContext = new FakeTenantContext(_tenantId);
        var searchUseCase = new FakeHybridSearchUseCase();
        var approvalManager = new InMemoryApprovalManager();

        var refusalResponse = new GroundedAnswerResponse(
            GroundedAnswerStatus.Refused,
            null,
            "Insufficient evidence available in government records.",
            Array.Empty<CitationItem>(),
            "Gemini",
            "N/A",
            TimeSpan.FromMilliseconds(20));

        var groundedUseCase = new FakeGroundedAnswerUseCase(refusalResponse);
        var draftTool = new DraftApprovalTool(approvalManager);
        var draftAgent = new ResponseDrafterAgent(groundedUseCase, NullLogger<ResponseDrafterAgent>.Instance);

        var options = Options.Create(new OrchestrationOptions { TimeoutSeconds = 10, EnableFallback = false });
        var orchestrator = new SequentialPipelineOrchestrator(
            new IAgent[] { draftAgent },
            new IAgentTool[] { draftTool },
            groundedUseCase,
            tenantContext,
            options,
            NullLogger<SequentialPipelineOrchestrator>.Instance);

        var events = new List<StreamProgressEvent>();
        await foreach (var evt in orchestrator.OrchestrateStreamAsync("Alien technology question"))
        {
            events.Add(evt);
        }

        Assert.DoesNotContain(events, e => e.EventType == StreamEventType.RunCompleted);
        var terminal = events.Last();
        Assert.Equal(StreamEventType.RunFailed, terminal.EventType);
        Assert.Contains("Insufficient evidence", terminal.ErrorMessage);
    }

    [Fact]
    public async Task OrchestrateStreamAsync_CancellationByCaller_EmitsRunCancelled_StopsPipeline_DoesNotTriggerFallback()
    {
        var tenantContext = new FakeTenantContext(_tenantId);
        var searchUseCase = new FakeHybridSearchUseCase();
        var approvalManager = new InMemoryApprovalManager();

        var fallbackUseCase = new FakeGroundedAnswerUseCase(new GroundedAnswerResponse(
            GroundedAnswerStatus.Grounded, "Fallback answer", null, Array.Empty<CitationItem>(), "P", "M", TimeSpan.Zero));

        using var cts = new CancellationTokenSource();

        var slowAgent = new CancellableSlowAgent("Eligibility Identifier Agent", onExecute: () =>
        {
            // Trigger caller cancellation during agent execution
            cts.Cancel();
        });

        var options = Options.Create(new OrchestrationOptions
        {
            TimeoutSeconds = 10,
            EnableFallback = true // Fallback is enabled, but caller cancellation must NOT trigger it!
        });

        var orchestrator = new SequentialPipelineOrchestrator(
            new IAgent[] { slowAgent },
            Array.Empty<IAgentTool>(),
            fallbackUseCase,
            tenantContext,
            options,
            NullLogger<SequentialPipelineOrchestrator>.Instance);

        var events = new List<StreamProgressEvent>();
        try
        {
            await foreach (var evt in orchestrator.OrchestrateStreamAsync("Query", cancellationToken: cts.Token))
            {
                events.Add(evt);
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation exception during consumer read is expected
        }

        // Invariant: Caller cancellation emits RunCancelled
        Assert.Contains(events, e => e.EventType == StreamEventType.RunCancelled);

        // Invariant: Caller cancellation NEVER invokes Plain-RAG fallback
        Assert.Equal(0, fallbackUseCase.CallCount);
        Assert.DoesNotContain(events, e => e.EventType == StreamEventType.FallbackStarted);
        Assert.DoesNotContain(events, e => e.EventType == StreamEventType.RunCompleted);
    }

    [Fact]
    public async Task OrchestrateStreamAsync_CancellationByCaller_DoesNotExecuteDraftApprovalTool_LeavesApprovalsPending()
    {
        var tenantContext = new FakeTenantContext(_tenantId);
        var approvalManager = new InMemoryApprovalManager();
        var searchUseCase = new FakeHybridSearchUseCase();

        // Pre-create a pending approval request to verify it remains pending after cancellation
        var pendingReq = await approvalManager.CreateRequestAsync(
            _tenantId, "Publish Test", "Content", CancellationToken.None);

        using var cts = new CancellationTokenSource();

        var cancellingAgent = new CancellableSlowAgent("Response Drafter Agent", onExecute: () =>
        {
            cts.Cancel();
        });

        var fallbackUseCase = new FakeGroundedAnswerUseCase(new GroundedAnswerResponse(
            GroundedAnswerStatus.Grounded, "Ans", null, Array.Empty<CitationItem>(), "P", "M", TimeSpan.Zero));

        var options = Options.Create(new OrchestrationOptions { TimeoutSeconds = 10 });
        var orchestrator = new SequentialPipelineOrchestrator(
            new IAgent[] { cancellingAgent },
            Array.Empty<IAgentTool>(),
            fallbackUseCase,
            tenantContext,
            options,
            NullLogger<SequentialPipelineOrchestrator>.Instance);

        try
        {
            await foreach (var evt in orchestrator.OrchestrateStreamAsync("Query", cancellationToken: cts.Token))
            {
            }
        }
        catch (OperationCanceledException)
        {
        }

        // Verify that the approval request remains untouched and is NOT executed
        var retrievedReq = await approvalManager.GetRequestAsync(pendingReq.RequestId, _tenantId, CancellationToken.None);
        Assert.NotNull(retrievedReq);
        Assert.Equal(ApprovalDecision.Pending, retrievedReq.Decision);
        Assert.False(retrievedReq.IsExecuted);
    }

    [Fact]
    public async Task OrchestrateStreamAsync_AgentFailure_WithFallbackEnabled_EmitsFallbackStarted_AndCompletesWithFallback()
    {
        var tenantContext = new FakeTenantContext(_tenantId);
        var failingAgent = new FailingAgent("Eligibility Identifier Agent");
        var approvalManager = new InMemoryApprovalManager();

        var fallbackResponse = new GroundedAnswerResponse(
            GroundedAnswerStatus.Grounded,
            "Grounded response produced via fallback.",
            null,
            new List<CitationItem> { new("f1", Guid.NewGuid(), Guid.NewGuid(), "fallback.pdf", "Guide", 1) },
            "GeminiFallback",
            "gemini-2.5-flash",
            TimeSpan.FromMilliseconds(30));

        var fallbackUseCase = new FakeGroundedAnswerUseCase(fallbackResponse);
        var options = Options.Create(new OrchestrationOptions
        {
            MaxRetries = 0,
            TimeoutSeconds = 10,
            EnableFallback = true
        });

        var orchestrator = new SequentialPipelineOrchestrator(
            new IAgent[] { failingAgent },
            Array.Empty<IAgentTool>(),
            fallbackUseCase,
            tenantContext,
            options,
            NullLogger<SequentialPipelineOrchestrator>.Instance);

        var events = new List<StreamProgressEvent>();
        await foreach (var evt in orchestrator.OrchestrateStreamAsync("Query"))
        {
            events.Add(evt);
        }

        Assert.Contains(events, e => e.EventType == StreamEventType.FallbackStarted);
        var terminal = events.Last();
        Assert.Equal(StreamEventType.RunCompleted, terminal.EventType);
        Assert.Equal("CompletedWithFallback", terminal.Status);
        Assert.NotNull(terminal.FinalResponse);
        Assert.Equal("Grounded response produced via fallback.", terminal.FinalResponse.Answer);
    }

    [Fact]
    public async Task OrchestrateStreamAsync_TerminalEvent_IsAlwaysOneOf_RunCompleted_RunFailed_RunCancelled()
    {
        var tenantContext = new FakeTenantContext(_tenantId);
        var failingAgent = new FailingAgent("Eligibility Identifier Agent");
        var fallbackUseCase = new FakeGroundedAnswerUseCase(new GroundedAnswerResponse(
            GroundedAnswerStatus.Refused, null, "Refused", Array.Empty<CitationItem>(), "P", "M", TimeSpan.Zero));

        var options = Options.Create(new OrchestrationOptions { MaxRetries = 0, EnableFallback = false });
        var orchestrator = new SequentialPipelineOrchestrator(
            new IAgent[] { failingAgent },
            Array.Empty<IAgentTool>(),
            fallbackUseCase,
            tenantContext,
            options,
            NullLogger<SequentialPipelineOrchestrator>.Instance);

        var events = new List<StreamProgressEvent>();
        await foreach (var evt in orchestrator.OrchestrateStreamAsync("Query"))
        {
            events.Add(evt);
        }

        var terminal = events.Last();
        Assert.True(
            terminal.EventType is StreamEventType.RunCompleted or StreamEventType.RunFailed or StreamEventType.RunCancelled,
            $"Expected terminal event to be RunCompleted, RunFailed, or RunCancelled, but got {terminal.EventType}");
    }

    [Fact]
    public async Task OrchestrateStreamAsync_ClientTenantSpoofingIgnored_AlwaysUsesTenantFromITenantContext()
    {
        var serverTenantId = Guid.NewGuid();
        var tenantContext = new FakeTenantContext(serverTenantId);
        var approvalManager = new InMemoryApprovalManager();

        var groundedResponse = new GroundedAnswerResponse(
            GroundedAnswerStatus.Grounded, "Valid answer", null, Array.Empty<CitationItem>(), "P", "M", TimeSpan.Zero);
        var groundedUseCase = new FakeGroundedAnswerUseCase(groundedResponse);

        var options = Options.Create(new OrchestrationOptions { TimeoutSeconds = 10 });
        var orchestrator = new SequentialPipelineOrchestrator(
            Array.Empty<IAgent>(),
            Array.Empty<IAgentTool>(),
            groundedUseCase,
            tenantContext,
            options,
            NullLogger<SequentialPipelineOrchestrator>.Instance);

        var events = new List<StreamProgressEvent>();
        await foreach (var evt in orchestrator.OrchestrateStreamAsync("Query"))
        {
            events.Add(evt);
        }

        // All events must carry server-side authenticated tenant ID
        foreach (var evt in events)
        {
            Assert.Equal(serverTenantId, evt.TenantId);
        }
    }

    [Fact]
    public async Task OrchestrateStreamAsync_ToolExecution_EmitsToolStarted_AndToolCompleted_Events()
    {
        var tenantContext = new FakeTenantContext(_tenantId);
        var searchUseCase = new FakeHybridSearchUseCase();
        var approvalManager = new InMemoryApprovalManager();

        var eligTool = new EligibilityLookupTool(searchUseCase);
        var eligAgent = new EligibilityIdentifierAgent(NullLogger<EligibilityIdentifierAgent>.Instance);

        var groundedUseCase = new FakeGroundedAnswerUseCase(new GroundedAnswerResponse(
            GroundedAnswerStatus.Grounded, "Done", null, Array.Empty<CitationItem>(), "P", "M", TimeSpan.Zero));

        var options = Options.Create(new OrchestrationOptions { TimeoutSeconds = 10 });
        var orchestrator = new SequentialPipelineOrchestrator(
            new IAgent[] { eligAgent },
            new IAgentTool[] { eligTool },
            groundedUseCase,
            tenantContext,
            options,
            NullLogger<SequentialPipelineOrchestrator>.Instance);

        var events = new List<StreamProgressEvent>();
        await foreach (var evt in orchestrator.OrchestrateStreamAsync("Check eligibility for grant"))
        {
            events.Add(evt);
        }

        var toolStarted = events.FirstOrDefault(e => e.EventType == StreamEventType.ToolStarted);
        var toolCompleted = events.FirstOrDefault(e => e.EventType == StreamEventType.ToolCompleted);

        Assert.NotNull(toolStarted);
        Assert.Equal("eligibility_lookup", toolStarted.ToolName);
        Assert.Equal("Eligibility Identifier Agent", toolStarted.AgentRole);

        Assert.NotNull(toolCompleted);
        Assert.Equal("eligibility_lookup", toolCompleted.ToolName);
        Assert.Equal("Completed", toolCompleted.Status);
    }

    /// <summary>
    /// Regression test for H-1 (bounded-channel silent-drop bug).
    /// A <see cref="StreamingFakeGroundedAnswerUseCase"/> that emits 600 AnswerChunk events
    /// exercises the path where <see cref="ChannelBasedEventSink"/> calls TryWrite() more than
    /// 512 times.  Before the fix (bounded Channel(512)), TryWrite() silently returned false for
    /// events 513+ and the terminal RunCompleted was also dropped, causing the consumer loop to
    /// block forever.  After the fix (unbounded Channel) every write succeeds immediately.
    /// </summary>
    [Fact]
    public async Task OrchestrateStreamAsync_MoreThan512Chunks_AllChunksDeliveredAndTerminalEventPresent()
    {
        const int ChunkCount = 600;

        var tenantContext = new FakeTenantContext(_tenantId);
        var groundedResponse = new GroundedAnswerResponse(
            GroundedAnswerStatus.Grounded,
            "Done.",
            null,
            Array.Empty<CitationItem>(),
            "P", "M", TimeSpan.Zero);

        // Build chunk list: 600 distinct one-word tokens.
        var chunks = Enumerable.Range(1, ChunkCount).Select(i => $"tok{i}").ToList();

        var groundedUseCase = new StreamingFakeGroundedAnswerUseCase(groundedResponse, chunks);
        var searchUseCase = new FakeHybridSearchUseCase();
        var approvalManager = new InMemoryApprovalManager();

        var eligAgent = new EligibilityIdentifierAgent(NullLogger<EligibilityIdentifierAgent>.Instance);
        var procAgent = new ProcedureResolverAgent(NullLogger<ProcedureResolverAgent>.Instance);
        var draftAgent = new ResponseDrafterAgent(groundedUseCase, NullLogger<ResponseDrafterAgent>.Instance);

        var docTool = new DocumentSearchTool(searchUseCase);
        var eligTool = new EligibilityLookupTool(searchUseCase);
        var procTool = new ProcedureLookupTool(searchUseCase);
        var draftTool = new DraftApprovalTool(approvalManager);

        var options = Options.Create(new OrchestrationOptions
        {
            MaxIterations = 5,
            TimeoutSeconds = 30,
            MaxRetries = 1,
            RetryBackoffMilliseconds = 10,
            EnableFallback = false
        });

        var orchestrator = new SequentialPipelineOrchestrator(
            new IAgent[] { draftAgent, eligAgent, procAgent },
            new IAgentTool[] { docTool, eligTool, procTool, draftTool },
            groundedUseCase,
            tenantContext,
            options,
            NullLogger<SequentialPipelineOrchestrator>.Instance);

        var events = new List<StreamProgressEvent>();
        await foreach (var evt in orchestrator.OrchestrateStreamAsync("Describe the full procedure."))
        {
            events.Add(evt);
        }

        // All AnswerChunk events must be present — none silently dropped.
        var chunkEvents = events.Where(e => e.EventType == StreamEventType.AnswerChunk).ToList();
        Assert.Equal(ChunkCount, chunkEvents.Count);

        // Terminal event must be present and must be last.
        var last = events.Last();
        Assert.Equal(StreamEventType.RunCompleted, last.EventType);
    }

    // --- Helpers and Fakes ---

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

        public FakeGroundedAnswerUseCase(GroundedAnswerResponse response) => _response = response;

        public Task<GroundedAnswerResponse> GetGroundedAnswerAsync(GroundedAnswerRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_response);
        }
    }

    private sealed class StreamingFakeGroundedAnswerUseCase : IGroundedAnswerUseCase
    {
        private readonly GroundedAnswerResponse _response;
        private readonly IReadOnlyList<string> _chunks;

        public StreamingFakeGroundedAnswerUseCase(GroundedAnswerResponse response, IReadOnlyList<string> chunks)
        {
            _response = response;
            _chunks = chunks;
        }

        public Task<GroundedAnswerResponse> GetGroundedAnswerAsync(GroundedAnswerRequest request, CancellationToken cancellationToken)
            => Task.FromResult(_response);

        public Task<GroundedAnswerResponse> GetGroundedAnswerAsync(
            GroundedAnswerRequest request, IOrchestrationEventSink? eventSink, CancellationToken cancellationToken)
        {
            if (eventSink != null)
            {
                foreach (var chunk in _chunks)
                {
                    eventSink.EmitChunk(chunk);
                }
            }
            return Task.FromResult(_response);
        }
    }

    private sealed class FailingAgent : IAgent
    {
        public string Role { get; }
        public string Description => "Fails always";
        public IReadOnlySet<string> AllowedToolNames => new HashSet<string>();
        public string InputContract => "Input";
        public string OutputContract => "Output";
        public string TerminationCondition => "Fail";

        public FailingAgent(string role) => Role = role;

        public Task<AgentExecutionResult> ExecuteAsync(AgentContext context, IReadOnlyDictionary<string, IAgentTool> availableTools, CancellationToken cancellationToken)
        {
            return Task.FromResult(new AgentExecutionResult(
                Role, false, "Execution failed.", Array.Empty<AgentToolCallRecord>(), TimeSpan.FromMilliseconds(10),
                ErrorMessage: "Simulated agent failure."));
        }
    }

    private sealed class CancellableSlowAgent : IAgent
    {
        private readonly Action _onExecute;
        public string Role { get; }
        public string Description => "Slow agent";
        public IReadOnlySet<string> AllowedToolNames => new HashSet<string>();
        public string InputContract => "Input";
        public string OutputContract => "Output";
        public string TerminationCondition => "Done";

        public CancellableSlowAgent(string role, Action onExecute)
        {
            Role = role;
            _onExecute = onExecute;
        }

        public async Task<AgentExecutionResult> ExecuteAsync(AgentContext context, IReadOnlyDictionary<string, IAgentTool> availableTools, CancellationToken cancellationToken)
        {
            _onExecute();
            await Task.Delay(5000, cancellationToken);
            return new AgentExecutionResult(Role, true, "Done", Array.Empty<AgentToolCallRecord>(), TimeSpan.Zero);
        }
    }
}
