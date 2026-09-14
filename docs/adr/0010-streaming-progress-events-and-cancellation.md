# ADR 0010: Streaming Progress Events, Incremental Token Delivery, and Responsive Cancellation

## Status

Accepted

## Context

Following ADR 0009 (Multi-Agent Orchestration Foundation and Human Approval Boundary), long-running orchestration pipelines across multiple agents and LLM inferences require real-time visibility and responsive user cancellation:
1. **Real-Time Client Visibility**: Clients need fine-grained lifecycle and progress updates (`RunStarted`, `AgentStarted`, `ToolStarted`, `ToolCompleted`, `AnswerChunk`, `ApprovalRequired`, `FallbackStarted`, `RunCompleted`, `RunFailed`, `RunCancelled`) without polling.
2. **Clean Architecture Separation**: Streaming and event publishing abstractions must remain purely in the Application layer, completely decoupled from HTTP, Server-Sent Events (SSE), ASP.NET Core, or web framework constructs.
3. **No Fragile Mutable State**: Eliminating mutable callback properties (`Action<string>`, `Action<string, string>`) on `AgentContext` in favor of thread-safe, immutable, channel-backed sinks.
4. **Real Provider-Side Streaming**: Token streaming must be genuinely incremental from upstream LLM providers (e.g. Google Gemini `:streamGenerateContent?alt=sse`) rather than simulated by post-hoc splitting of a buffered completion.
5. **Final-Answer & Citation Integrity**: Partial streamed text (`AnswerChunk`) represents unvalidated intermediate tokens. `RunCompleted` must ONLY represent a successfully validated final response after full citation validation and refusal policies have completed. Invalid citation output or refusal must NEVER emit `RunCompleted`.
6. **Cooperative Cancellation & Safety Invariants**:
   - `CancellationToken` must propagate through every asynchronous boundary.
   - Caller cancellation must immediately stop further agent and tool execution.
   - Explicit caller cancellation must emit `RunCancelled` and **never** invoke Plain-RAG fallback.
   - Explicit caller cancellation must never execute side-effecting approval actions; pending approvals remain pending.
7. **SSE Wire Safety & Multi-Tenancy**:
   - Tenant context must be derived exclusively from `ITenantContext`. Client-supplied tenant IDs or headers (e.g. `X-Tenant-ID`) are strictly ignored.
   - Streaming payloads must never expose secrets, API keys, system prompts, or raw full documents. Error payloads must be strictly bounded.

## Decision

1. **Application Streaming Abstractions (`Application/Streaming/Abstractions`)**:
   - `IStreamingOrchestrator`: Defines `IAsyncEnumerable<StreamProgressEvent> OrchestrateStreamAsync(string userQuery, string? correlationId = null, CancellationToken cancellationToken = default)`.
   - `IOrchestrationEventSink`: Defines `void Emit(StreamProgressEvent progressEvent)` and `void EmitChunk(string chunk)`.
   - `ChannelBasedEventSink`: Thread-safe, bounded channel implementation (`System.Threading.Channels.Channel<StreamProgressEvent>`) connecting background orchestration execution to the async enumerable reader.

2. **Provider-Side Incremental Streaming (`Infrastructure/LLM/Providers`)**:
   - `IChatCompletionProvider.StreamCompleteAsync`: Yields genuine incremental tokens via `IAsyncEnumerable<string>`.
   - `GeminiChatCompletionProvider.StreamCompleteAsync`: Uses the Gemini API streaming endpoint `:streamGenerateContent?alt=sse` with `HttpCompletionOption.ResponseHeadersRead`, streaming SSE lines and emitting tokens as they arrive from Google's servers.

3. **Lifecycle Events & Ordering**:
   - Deterministic event ordering: `RunStarted` → `AgentStarted` → (optional `ToolStarted`/`ToolCompleted`/`AnswerChunk`/`ApprovalRequired`) → Terminal Event (`RunCompleted` | `RunFailed` | `RunCancelled`).
   - Terminal event guarantee: The final event in every stream is guaranteed to be one of `RunCompleted`, `RunFailed`, or `RunCancelled`.

4. **Final-Answer Integrity Gate**:
   - Tokens emitted during LLM streaming are published as `AnswerChunk` events marked as partial/streaming.
   - Full accumulated content undergoes citation validation against retrieved evidence chunks.
   - If citation validation fails or the answer is refused:
     - `GroundedAnswerResponse.Status` is `Refused`.
     - The terminal event emitted is `RunFailed` (with failure/refusal reason). `RunCompleted` is never emitted.
   - If citation validation passes:
     - The terminal event emitted is `RunCompleted` with `FinalResponse` attached.

5. **Cancellation Invariants**:
   - When caller cancels:
     - Linked tokens abort ongoing agent and tool tasks.
     - Terminal event emitted is `RunCancelled`.
     - Plain-RAG fallback is strictly disabled and never invoked.
     - `DraftApprovalTool` is never executed; pending approvals remain untouched.

6. **API Streaming Endpoint (`API/Endpoints/OrchestrationEndpoints`)**:
   - `POST /api/orchestrate/stream`: Delivers events formatted as standard Server-Sent Events (`text/event-stream`).
   - Tenant scoping is enforced via server-side `ITenantContext`; client headers/body tenant overrides are ignored.
   - Client disconnections are handled cooperatively without unhandled exceptions.

## Consequences

- Clients receive immediate, real-time feedback during multi-agent analysis and LLM generation.
- Strong grounding and citation invariants are preserved even in streaming scenarios.
- Strict multi-tenancy and credential safety are maintained across all streaming endpoints and event records.
