# Architecture — Government Domain Copilot

## 1. Scope and reading guide

This architecture record describes the current D4 Government Domain Copilot MVP
and separates it from the intended target system. It uses the assessment terms
**T0**, **grounded/never-guessing**, **approval**, **observability**, and
**corpus** consistently with [BRD.md](BRD.md) and
[SYSTEM-DESIGN.md](SYSTEM-DESIGN.md).

Every diagram has editable Mermaid source committed under
[`docs/architecture/`](architecture/). The linked source files are the canonical
diagram artifacts; they are intentionally kept separate so reviewers can render
or revise them without redrawing an image.

## 2. C4 Level 1 — System context

**Current MVP:** officers and supervisors use the protected system API; it
persists tenant-scoped data in PostgreSQL/pgvector, reads the committed synthetic
corpus through CLI tooling, and calls Gemini or a configured local Ollama server
through provider adapters. The citizen is a beneficiary of the officer workflow,
not an anonymous MVP API user.

**Target-only:** a production identity provider and governed official source
ingestion are shown with dashed styling. Neither is implemented or represented as
a live deployment dependency.

Editable diagram: [C4 Level 1 system context](architecture/c4-context.mmd).

## 3. C4 Level 2 — Containers

The runnable assessment stack is ASP.NET Core API + one-shot migration process +
PostgreSQL/pgvector. `ClientCli` is a separately invocable process and an opt-in
Compose `tools` profile service; it is not a long-running normal-stack container.
`EvaluationRunner` is a local .NET CLI. The repository contains an Angular web
project, but it is not part of the assessment Compose runtime, so no frontend is
claimed as a current deployed container.

PostgreSQL is intentionally reachable only inside the Compose network. The API
is the published port; the configured Gemini/Ollama provider remains outside the
application trust boundary.

Editable diagram: [C4 Level 2 containers](architecture/c4-container.mmd).

Evidence: [docker-compose.yml](../docker-compose.yml),
[API Program](../src/API/Program.cs), [Client CLI](../src/ClientCli/Program.cs),
and [Evaluation Runner](../src/EvaluationRunner/Program.cs).

## 4. C4 Level 3 — Components

The API exposes minimal endpoints and applies authentication/authorization and
correlation middleware. Application owns use cases and framework-independent
contracts. Infrastructure implements persistence/retrieval/provider adapters.
The central grounded-answer path uses `IChatCompletionProvider`, so Gemini and
Ollama are replaceable Infrastructure implementations rather than Application or
Domain behavior.

Editable diagram: [C4 Level 3 components](architecture/c4-component.mmd).

| Component area | Current implementation evidence |
|---|---|
| API routes, exception safety, health/readiness | [Program.cs](../src/API/Program.cs), [Endpoints](../src/API/Endpoints/) |
| Authentication, RBAC, tenant resolution | [ApiKeyAuthenticationHandler.cs](../src/Infrastructure/Auth/ApiKeyAuthenticationHandler.cs), [CurrentUserContext.cs](../src/Infrastructure/Auth/CurrentUserContext.cs), [DevelopmentTenantContext.cs](../src/Infrastructure/Tenant/DevelopmentTenantContext.cs), [SECURITY.md](SECURITY.md) |
| Ingestion/chunking | [IngestDocumentUseCase.cs](../src/Application/Documents/IngestDocumentUseCase.cs), [DeterministicDocumentChunker.cs](../src/Infrastructure/Documents/DeterministicDocumentChunker.cs) |
| Hybrid retrieval | [HybridSearchUseCase.cs](../src/Application/Retrieval/HybridSearchUseCase.cs), [PgVectorChunkRetriever.cs](../src/Infrastructure/Retrieval/PgVectorChunkRetriever.cs), [PgKeywordChunkRetriever.cs](../src/Infrastructure/Retrieval/PgKeywordChunkRetriever.cs) |
| Grounded answer/citations | [GroundedAnswerUseCase.cs](../src/Application/Answering/Services/GroundedAnswerUseCase.cs), [IChatCompletionProvider.cs](../src/Application/Answering/Abstractions/IChatCompletionProvider.cs), [ADR 0008](adr/0008-grounded-answer-generation-and-citations.md) |
| D4 orchestration/approval/streaming | [Orchestration endpoints](../src/API/Endpoints/OrchestrationEndpoints.cs), [orchestration.md](orchestration.md), [ADR 0009](adr/0009-multi-agent-orchestration-and-human-approval.md), [ADR 0010](adr/0010-streaming-progress-events-and-cancellation.md) |
| Database, sessions, runs, LLM traces | [GovernmentDomainCopilotDbContext.cs](../src/Infrastructure/Persistence/GovernmentDomainCopilotDbContext.cs), [OBSERVABILITY.md](OBSERVABILITY.md), [ADR 0011](adr/0011-session-history-and-run-traces.md) |
| Provider selection/adapters | [DependencyInjection.cs](../src/Infrastructure/DependencyInjection.cs), [GeminiChatCompletionProvider.cs](../src/Infrastructure/LLM/Providers/GeminiChatCompletionProvider.cs), [OllamaChatCompletionProvider.cs](../src/Infrastructure/LLM/Providers/OllamaChatCompletionProvider.cs) |

## 5. End-to-end D4 sequence — streaming and approval

The following current-MVP flow focuses on the protected streaming orchestration
path. The direct answer endpoint uses the same retrieval and grounded-answer
rules without the orchestration stream. `AnswerChunk` events are partial output;
only a terminal result after citation validation represents the finalized answer.
Approval blocks a consequential staged action, not evidence retrieval or a
non-consequential officer-facing draft.

Editable diagram: [D4 streaming and approval sequence](architecture/d4-streaming-approval-sequence.mmd).

Key safety rules in the sequence:

- The API resolves the tenant from authentication before tenant-scoped work.
- Insufficient evidence prevents an LLM call; invalid citations produce refusal.
- Cancellation stops work and must not trigger fallback or approval execution.
- A response draft may create a pending approval, but only a supervisor may
  decide and execute it. Current execution records the approved staged outcome;
  it does not call a real external government system.

Evidence: [GroundedAnswerUseCase.cs](../src/Application/Answering/Services/GroundedAnswerUseCase.cs),
[OrchestrationEndpoints.cs](../src/API/Endpoints/OrchestrationEndpoints.cs),
[orchestration.md](orchestration.md), and [ADR 0010](adr/0010-streaming-progress-events-and-cancellation.md).

## 6. Trust boundaries

Editable diagram: [Trust-boundary diagram](architecture/trust-boundaries.mmd).

| Boundary | What crosses it | Control / limitation |
|---|---|---|
| Client → API | Query, API credential, and optional correlation ID. | API authentication and RBAC establish the identity. Client-supplied tenant IDs, provider URLs, and approval authority are not trusted. |
| API → Application/persistence | Authenticated server-side tenant context and validated request data. | `ITenantContext` scopes tenant-owned reads/writes, retrieval, sessions, approvals, and traces. T0 is never selected from a body/header/query/CLI override. |
| Persistence → grounded-answer context | Retrieved chunks from the authenticated tenant. | Evidence is bounded and treated as untrusted factual data, never executable instructions. |
| Application → LLM provider | System instruction, user question, bounded evidence context, model/generation settings, and Gemini API credential when Gemini is selected. | The LLM can see that generation payload only. It cannot directly access PostgreSQL, the full corpus, API endpoints, session/approval stores, authenticated tenant-control context, or approval authority. It may infer information only from the supplied text. |
| Configuration → runtime | Database connection string, provider choice/base URL, model/limits, and Gemini credential. | Configuration is server-side. `GEMINI_API_KEY` is environment supplied; Ollama has no API key. No client endpoint supplies an LLM provider URL. |
| Approval boundary | A staged draft and supervisor decision. | A model or officer cannot execute a pending/rejected action. `SupervisorOnly` protects decision/execution; the MVP has no real-world government-system connector. |
| Observability boundary | Correlation/run/provider/timing/status and available usage metadata. | Persisted traces exclude secrets, authorization headers, system prompts, raw chunks, and partial stream output; errors are sanitized. |

The detailed threat model, synthetic-identity boundary, and endpoint matrix are
authoritative in [SECURITY.md](SECURITY.md). Grounding and prompt-injection
controls are in [ADR 0008](adr/0008-grounded-answer-generation-and-citations.md).

## 7. ER/data model

The diagram uses actual `GovernmentDomainCopilotDbContext` table/entity names.
`ConversationSessions`/`Runs`/`Approvals` and `UserSessions`/`SessionMessages`/
`OrchestrationRunTraces` are both represented because both sets are currently
mapped by the DbContext. The diagram shows only relationships configured by EF
Core; fields such as `SessionId` and `RunId` stored on trace records are not
shown as foreign keys where the model does not configure them as such.

Editable diagram: [EF Core data model](architecture/data-model.mmd).

Important persistence facts:

- `Documents` are uniquely identified by `(TenantId, SourceReference)` for
  idempotent corpus re-ingestion.
- `DocumentChunks` carry `vector(768)` embedding data and a computed full-text
  `SearchVector` under Npgsql/pgvector.
- Durable sessions, messages, run traces, and LLM invocation traces are all
  tenant-scoped. Trace stores persist finalized/safe metadata rather than partial
  stream text or secrets.

Evidence: [GovernmentDomainCopilotDbContext.cs](../src/Infrastructure/Persistence/GovernmentDomainCopilotDbContext.cs),
[CORPUS.md](CORPUS.md), and [ADR 0011](adr/0011-session-history-and-run-traces.md).

## 8. Layer dependency rules

Editable diagram: [Layer dependencies](architecture/layer-dependencies.mmd).

Allowed dependency direction is:

```text
Domain ← Application ← Infrastructure ← API / ClientCli
```

`API` and `ClientCli` compose Application and Infrastructure at the outer edge.
Domain has no EF Core, ASP.NET Core, database/vector SDK, provider SDK, or web
framework dependency. Application depends on Domain abstractions and does not
depend on Infrastructure or API. External AI, embedding, and database access is
implemented through Infrastructure adapters.

Evidence: [Application dependency injection](../src/Application/DependencyInjection.cs),
[Infrastructure dependency injection](../src/Infrastructure/DependencyInjection.cs),
project files under `src/`, and [AGENTS.md](../AGENTS.md).

## 9. Architecture decisions

Existing decisions are maintained in the ADR set rather than duplicated here:

- [ADR 0001](adr/0001-postgresql-ef-core-foundation.md) — PostgreSQL/EF Core.
- [ADR 0002](adr/0002-deterministic-document-chunking.md) — deterministic
  document chunking.
- [ADR 0003](adr/0003-embedding-provider-abstractions.md) and
  [ADR 0004](adr/0004-pgvector-embedding-persistence.md) — embedding adapters
  and pgvector persistence.
- [ADRs 0005–0007](adr/) — tenant-scoped vector/hybrid retrieval and reranking.
- [ADR 0008](adr/0008-grounded-answer-generation-and-citations.md) — grounded
  generation, citations, evidence refusal, and prompt-injection handling.
- [ADR 0009](adr/0009-multi-agent-orchestration-and-human-approval.md) — D4
  sequential orchestration and human approval.
- [ADR 0010](adr/0010-streaming-progress-events-and-cancellation.md) — true
  streaming, terminal integrity, and cancellation.
- [ADR 0011](adr/0011-session-history-and-run-traces.md) — durable sessions and
  run traces.

### Architecture note — PR #27 chat provider selection

No existing ADR specifically records PR #27's chat-completion provider selection.
The current architecture uses the existing Application
`IChatCompletionProvider` abstraction; Infrastructure registers Gemini and
Ollama implementations and selects exactly one through
`LlmProviders:PrimaryProvider`. Gemini remains the default and unknown values
fail fast. This is an implemented architecture note, not a retrospective ADR;
future changes to provider policy, fallback, credentials, or deployment exposure
should receive a dedicated ADR.

Evidence: [LlmProviderOptions.cs](../src/Infrastructure/LLM/Providers/LlmProviderOptions.cs),
[DependencyInjection.cs](../src/Infrastructure/DependencyInjection.cs), and
[provider-swap acceptance tests](../tests/Integration.Tests/Answering/ChatProviderSwapAcceptanceTests.cs).

## 10. Current MVP versus target architecture

The current MVP implements the D4 workflow foundation, T0 tenant-scoped
persistence/retrieval, grounding/refusal, staged approval, streaming, provider
adapters, accountability traces, and an assessment-scale synthetic corpus. It
does **not** implement a production identity provider, official-source
governance/refresh, a public citizen frontend, bundled local-model infrastructure,
or real-world government-system execution. Those are target/deployment decisions,
not current components.

See [SYSTEM-DESIGN.md](SYSTEM-DESIGN.md#4-target-vs-mvp-gap-table) for the
reviewer-oriented target/MVP gap table and [BRD.md](BRD.md#current-mvp-boundaries-and-follow-on-decisions)
for business scope boundaries.
