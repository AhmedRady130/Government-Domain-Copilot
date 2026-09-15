# System Design — Government Domain Copilot

## 1. Purpose and scope

Government Domain Copilot is a D4 (Government — citizen services and
regulations) assessment system. It supports an authorized officer researching a
citizen situation and producing an evidence-grounded, cited response draft about
services, eligibility, documents, fees, timelines, and procedures. It must
never guess government facts, must isolate tenants (T0), and must retain human
approval for consequential actions.

This document distinguishes the **target system** from the **implemented MVP**.
It is a reviewer-oriented overview, not a replacement for the detailed business,
security, corpus, evaluation, orchestration, observability, and ADR documents.

## 2. Part A — Target System

### Intended end-state architecture

The target is a secure, multi-tenant government-service copilot with a layered
application core. Channel/API hosts authenticate users and resolve authoritative
tenant identity; application use cases coordinate policy; infrastructure adapters
provide persistence, retrieval, provider integration, and observability. The
assistant uses only tenant-authorized, governed government-service evidence and
returns source-linked drafts rather than unsupported determinations.

### Major target capabilities

- Ingest and govern approved public government knowledge sources.
- Search and rank tenant-scoped service, eligibility, document, fee, and
  timeline information.
- Generate grounded answers with exact source citations and evidence-based
  refusal when evidence is insufficient.
- Orchestrate bounded D4 research/drafting steps with explicit tool allow-lists.
- Stage consequential actions for human supervisor review and execution.
- Stream safe progress and answer events with cancellation.
- Preserve accountable, tenant-scoped sessions, runs, and LLM invocation traces.
- Support provider adapters without changing Domain or Application behavior.

### Target actors and workflows

Citizens are the eventual beneficiaries; authorized officers research and draft
responses; supervisors approve or reject consequential drafts; tenant operators
manage identities, data sources, configuration, and audit access. A typical
target workflow is citizen situation → officer request → authenticated,
tenant-scoped retrieval → grounded draft → supervisor decision → approved,
audited action where applicable.

### Target security and trust model

Tenant identity is derived only from a trusted, server-side authenticated
principal. All tenant data access is constrained at the persistence/query layer.
External model providers are untrusted external services: they receive only the
bounded generation request, not database or tenant-control access. Retrieved
documents are untrusted data and cannot override system/application instructions.
Consequential authority remains with an authenticated human supervisor.

### Target observability and evaluation posture

The target maintains correlation/run identifiers, lifecycle status, bounded
agent/tool activity, retrieved evidence references, provider/model/timing, and
available token/cost information. It excludes secrets and unnecessary raw
content. Evaluation includes reproducible retrieval, grounding, refusal, safety,
tenant-isolation, and provider-swap tests against governed datasets.

## 3. Part B — Implemented MVP

### Components and layer responsibilities

| Layer/component | Current responsibility |
|---|---|
| `src/Domain` | Core entities, invariants, roles, and framework-independent business concepts. |
| `src/Application` | Use cases and abstractions for ingestion, retrieval, grounded answers, agents, approvals, sessions, streaming, and observability. |
| `src/Infrastructure` | EF Core/Npgsql/pgvector persistence, chunking, retrieval repositories, authentication/tenant contexts, LLM/embedding adapters, and PostgreSQL-backed stores. |
| `src/API` | ASP.NET Core minimal API host, authentication/authorization, request contracts, correlation middleware, health/readiness, and SSE formatting. |
| `src/ClientCli` | Corpus validation and authenticated corpus seeding. |
| `src/EvaluationRunner` | Evaluation-harness command-line execution against the committed golden dataset. |
| `docker-compose.yml` | PostgreSQL, one-shot migrations, API, and opt-in CLI runtime wiring. |

The dependency direction is Domain ← Application ← Infrastructure/API. Provider,
database, and web dependencies are kept outside Domain and Application-facing
abstractions.

### API capabilities

The protected API includes document ingestion and document lookup, hybrid search,
grounded answers, orchestration (including streaming), approval lifecycle,
session history/messages, run lookup, and LLM trace lookup. The concrete route
definitions are in `src/API/Endpoints/`:

- Documents: `POST /api/documents`, `GET /api/documents/{documentId}`
- Retrieval and answers: `GET /api/search`, `POST /api/answer`
- D4 orchestration: `POST /api/orchestrate`, `POST /api/orchestrate/stream`
- Approval: `GET /api/approvals`, `GET /api/approvals/{requestId}`, `POST
  /api/approvals/{requestId}/decide`, `POST /api/approvals/{requestId}/execute`
- Sessions: `POST/GET /api/sessions`, session detail and message routes
- Runs and traces: `GET /api/runs`, `GET /api/runs/{runId}`, `GET
  /api/traces/llm`
- Operational checks: anonymous `GET /health`, `GET /health/live`, and `GET
  /ready`

All business routes require authorization; approval decision/execution require
the `SupervisorOnly` policy. The complete authorization matrix is in
[SECURITY.md](SECURITY.md).

### Persistence, ingestion, and retrieval

`GovernmentDomainCopilotDbContext` persists tenants, users, documents, chunks,
legacy conversation/run/approval/audit entities, durable session/message entities,
orchestration run traces, and LLM invocation traces. PostgreSQL uses pgvector for
768-dimensional embeddings and PostgreSQL full-text search for keyword retrieval.
Tenant-owned entities use tenant-aware keys/indexes and tenant filtering.

The ingestion use case normalizes document text, deterministically chunks it,
persists document/chunk records, and generates/persists embeddings through
application abstractions. Retrieval combines tenant-scoped vector and keyword
branches using reciprocal-rank fusion, then applies deterministic reranking.
The design decisions are recorded in ADRs 0001–0007.

### Grounded answer generation

`GroundedAnswerUseCase` runs hybrid retrieval, applies an evidence-sufficiency
policy before provider invocation, creates a bounded evidence context, calls
`IChatCompletionProvider`, then structurally validates citations against the
retrieved evidence. It returns a grounded response only when validation succeeds;
otherwise it returns a refusal. The system prompt explicitly treats evidence as
untrusted data and requires citation markers. ADR 0008 and
[evaluation.md](evaluation.md) describe the policy and tests.

### D4 orchestration, approval, and streaming

The implemented named pattern is a sequential pipeline:
`EligibilityIdentifierAgent` → `ProcedureResolverAgent` →
`ResponseDrafterAgent`. Its tool allow-lists cover tenant-scoped document search,
eligibility lookup, procedure lookup, and staging a draft approval. Execution is
bounded by iterations, timeout, retries/backoff, and cancellation; an eligible
failure can fall back to the grounded-answer workflow without bypassing safety.

Draft approval is staging-only. A pending request cannot execute until an
authenticated supervisor submits a decision; execution records a staged result
and does not call a real-world government system. `POST /api/orchestrate/stream`
uses SSE. Provider-side token deltas become `AnswerChunk` events; terminal events
are emitted only after final grounding/citation checks. Details are in
[orchestration.md](orchestration.md), ADR 0009, and ADR 0010.

### Sessions, runs, traces, and accountability

Sessions and messages are durable PostgreSQL records. Orchestration run traces
record correlation/run IDs, tenant scope, pattern, timing, status, iterations,
fallback, agent/tool outcomes, pending approval, and final-response metadata.
LLM traces record provider/model, operation type, timing, success, nullable usage
and estimated cost, and sanitized errors. Partial stream chunks, API keys,
authorization headers, system prompts, and raw chunks are not persisted. See
[OBSERVABILITY.md](OBSERVABILITY.md) and ADR 0011.

### Authentication, RBAC, and tenant isolation

The API-key handler resolves supported credentials into claims, including the
tenant and role. In production, tenant resolution uses the authenticated current
user context and fails closed; Development/Testing can use synthetic identities
for test/demo workflows. A client `tenantId`, `X-Tenant-ID`, query parameter, or
CLI override is not trusted for authorization. Tenant A and Tenant B synthetic
identities support isolation tests. See [SECURITY.md](SECURITY.md).

### Gemini/Ollama configuration

`IChatCompletionProvider` is an Application abstraction. Infrastructure registers
both `GeminiChatCompletionProvider` and `OllamaChatCompletionProvider`, selecting
the implementation through `LlmProviders:PrimaryProvider`. Gemini is the default;
an unknown value fails fast. Gemini reads its API key from environment
configuration; Ollama does not require an API key. Both provider adapters support
bounded requests, timeout/cancellation, non-streaming completion, and real
provider streaming. `OllamaBaseUrl` is server-side configuration only and cannot
be supplied by clients. Embedding provider selection is a separate concern.

### Corpus, tooling, and Docker Compose runtime

The committed synthetic D4 corpus has 32 documents and 160 explicit pages, split
16 documents each across synthetic Tenant A and Tenant B. `validate-corpus` is a
pure manifest/filesystem validation command and does not initialize PostgreSQL or
EF Core. `seed-corpus` requires an authenticated API key, rejects tenant
override, and re-ingests idempotently by tenant/source reference.

Compose starts internal PostgreSQL, a one-shot migration service, and API;
PostgreSQL is not published to the host. The `cli` service uses the same internal
connection-string convention but is in the `tools` profile and runs only when
explicitly invoked with `docker compose run --rm cli ...`. See
[CORPUS.md](CORPUS.md) and `docker-compose.yml`.

## 4. Target vs MVP gap table

| Capability | Target | MVP Status | Evidence | Gap / next step |
|---|---|---|---|---|
| D4 evidence-grounded response | Cited service guidance without fabricated facts. | **Fully implemented** for supported evidence/refusal flow. | `GroundedAnswerUseCase`, ADR 0008, [evaluation.md](evaluation.md). | Structural citation validation is not semantic/legal proof. |
| T0 multi-tenancy | Trusted production identity and isolation across all tenant data. | **Partially implemented**: tenant-scoped design and two synthetic test tenants; production identity source deferred. | [SECURITY.md](SECURITY.md), tenant-scoped repositories/tests. | Integrate and operate a production identity provider/credential store. |
| D4 orchestration | Governed service research and drafting workflow. | **Fully implemented foundation**. | [orchestration.md](orchestration.md), ADR 0009. | Agent knowledge remains grounded in the configured corpus; no external case-management integration. |
| Approval authority | Human approval before consequential action. | **Fully implemented** staged approval boundary. | Approval endpoints, ADR 0009. | Real-world government action connectors are intentionally deferred. |
| Streaming | Safe real-time progress, token delivery, and cancellation. | **Fully implemented**. | SSE endpoint, provider streaming, ADR 0010. | Clients must still treat partial chunks as unvalidated until terminal event. |
| Corpus | Governed public/synthetic corpus at assessment scale. | **Fully implemented** for synthetic assessment data. | [CORPUS.md](CORPUS.md), corpus validator/manifest. | Live official source governance and refresh process are intentionally deferred. |
| Evaluation | Reproducible quality/safety evaluation. | **Partially implemented**. | [evaluation.md](evaluation.md), golden dataset/tests. | Expand labeled corpus, adversarial coverage, and production monitoring thresholds. |
| Observability/accountability | End-to-end safe auditability. | **Fully implemented foundation**. | [OBSERVABILITY.md](OBSERVABILITY.md), ADR 0011. | Pricing catalog and retention/operations policy need production governance. |
| Provider portability | Hosted/local provider adapters with unchanged application workflow. | **Fully implemented** for Gemini/Ollama chat adapters. | DI selection, provider tests, swap acceptance test. | No bundled Ollama service/model or live-provider availability guarantee. |
| Citizen portal | Public, accessible self-service experience. | **Not implemented**. | `src/Web` is excluded from assessment Compose stack. | Define and implement a governed public-channel experience. |
| Production deployment governance | Production IAM, data governance, operations, and incident controls. | **Intentionally deferred**. | [SECURITY.md](SECURITY.md), [BRD.md](BRD.md). | Establish deployment architecture, identity, source governance, retention, and operations controls. |

## 5. Data and request flow

1. A citizen situation is submitted by an authenticated officer through an API
   workflow; the citizen is currently an indirect beneficiary rather than an
   anonymous API actor.
2. API authentication resolves claims and the server-side `ITenantContext`; the
   request cannot select a tenant.
3. For orchestration, the sequential D4 agents execute only their typed,
   allow-listed tools within configured limits. A direct answer follows the
   grounded-answer use case.
4. Hybrid retrieval creates embeddings as needed and executes vector and keyword
   search constrained to the resolved tenant. RRF and deterministic reranking
   produce bounded evidence candidates.
5. The evidence-sufficiency policy either refuses before model invocation or
   builds a bounded context and invokes the configured chat adapter.
6. Citation validation maps provider output only to retrieved citation markers.
   A grounded answer is returned only after this validation; otherwise the result
   is a refusal.
7. The response drafter stages a pending approval when applicable. A supervisor
   must decide before an approved staged action can execute.
8. The non-streaming API returns the finalized response. The SSE endpoint emits
   lifecycle/tool/partial-answer events and then a terminal result after final
   validation; caller cancellation terminates safely.
9. PostgreSQL persists eligible sessions, finalized messages, run traces, and
   safe LLM invocation metadata, all tenant-scoped and correlated.

## 6. Trust boundaries

| Boundary | Enforced behavior |
|---|---|
| Client → API | The client supplies a query/command, not authoritative tenant identity, approval authority, provider URL, or credentials for downstream providers. Authentication/RBAC occur at the API boundary. |
| API/Application → tenant data | `ITenantContext` resolves the authenticated tenant; repositories and retrieval queries apply tenant scope. Cross-tenant access must fail. |
| Application → LLM provider | The provider can see the system instruction, user question, bounded retrieved evidence context, selected model/generation settings, and provider authentication material where required. It does not directly receive a database connection, tenant ID as an authorization control, API session, approval authority, or unrestricted corpus access. |
| Retrieved evidence → prompt | Retrieved text is untrusted data. It is bounded and presented under the system/application instruction hierarchy; embedded instructions are not executable. |
| Secrets/configuration → runtime | Gemini credentials and connection details come from environment/configuration, not API request bodies or source-controlled secrets. Ollama requires no API key, but its base URL remains server-side configuration. |
| Approval boundary | Model output and officers may create a staged draft, but only an authenticated supervisor can decide or execute an approved request. No real-world government system is invoked by the MVP execution path. |
| Observability boundary | Traces retain scoped metadata and sanitized errors, not secrets, authorization headers, system prompts, raw evidence, or partial streamed output. |

## 7. Key design decisions

The detailed rationale is intentionally maintained in the existing ADRs:

- [ADR 0001](adr/0001-postgresql-ef-core-foundation.md): PostgreSQL/EF Core
  foundation.
- [ADR 0002](adr/0002-deterministic-document-chunking.md): deterministic
  chunking.
- [ADR 0003](adr/0003-embedding-provider-abstractions.md) and
  [ADR 0004](adr/0004-pgvector-embedding-persistence.md): embedding adapters
  and pgvector persistence.
- [ADR 0005](adr/0005-vector-similarity-retrieval.md),
  [ADR 0006](adr/0006-hybrid-retrieval-reciprocal-rank-fusion.md), and
  [ADR 0007](adr/0007-deterministic-retrieval-reranking.md): tenant-scoped
  retrieval and reranking.
- [ADR 0008](adr/0008-grounded-answer-generation-and-citations.md): grounded
  answers, refusal, citations, and prompt-injection handling.
- [ADR 0009](adr/0009-multi-agent-orchestration-and-human-approval.md): bounded
  D4 orchestration and approval boundary.
- [ADR 0010](adr/0010-streaming-progress-events-and-cancellation.md): real
  streaming, terminal integrity, and cancellation.
- [ADR 0011](adr/0011-session-history-and-run-traces.md): durable session and
  run trace persistence.

## 8. Constraints and assumptions

- The assessment corpus is synthetic, non-authoritative demo/training data; it
  contains no real personal data.
- Development synthetic keys are not production credentials. A production
  deployment must provide a trusted identity source and appropriate key storage.
- Docker Compose provides local reproducibility, not a production deployment
  topology; it does not start an Ollama server.
- Provider configuration, timeout, token limits, and models are operational
  settings. Live provider access is not assumed by deterministic tests.
- Grounding controls constrain the response to retrieved evidence, but the MVP
  does not claim a formal proof of factual correctness or legal authority.

## 9. Known limitations and technical debt

- Production IAM, tenant provisioning, credential rotation, and operator
  governance are deployment work, not implemented product capabilities.
- The corpus is assessment-scale synthetic content; there is no live official
  data ingestion, approval, provenance refresh, or retention program.
- The public citizen-facing experience is not implemented in the assessment
  stack.
- Citation validation is structural; broader semantic evaluation and adversarial
  testing remain areas for expansion.
- Ollama is an optional external/local dependency. The repository includes an
  adapter and deterministic tests, not a bundled model runtime.
- Current pricing configuration is Gemini-focused; unavailable provider usage or
  pricing remains nullable rather than invented.

## 10. Implementation evidence

| Area | Primary evidence |
|---|---|
| Business requirements and MVP boundary | [BRD.md](BRD.md) |
| API composition and endpoint routes | `src/API/Program.cs`, `src/API/Endpoints/` |
| Application use cases and contracts | `src/Application/Answering/`, `src/Application/Retrieval/`, `src/Application/Agents/`, `src/Application/Streaming/` |
| Persistence/retrieval and DI | `src/Infrastructure/Persistence/GovernmentDomainCopilotDbContext.cs`, `src/Infrastructure/Retrieval/`, `src/Infrastructure/DependencyInjection.cs` |
| Gemini/Ollama chat adapters | `src/Infrastructure/LLM/Providers/`, `tests/Integration.Tests/Answering/` |
| Authentication and tenancy | [SECURITY.md](SECURITY.md), `src/Infrastructure/Auth/`, `src/Infrastructure/Tenancy/` |
| Corpus tooling and runtime | [CORPUS.md](CORPUS.md), `src/ClientCli/`, `data/corpus/`, `docker-compose.yml` |
| Evaluation | [evaluation.md](evaluation.md), `src/EvaluationRunner/` |
| Orchestration/approval/streaming | [orchestration.md](orchestration.md), ADRs 0009–0010 |
| Observability | [OBSERVABILITY.md](OBSERVABILITY.md), ADR 0011 |
