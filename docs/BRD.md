# Business Requirements Document — Government Domain Copilot

## Purpose and business context

Government Domain Copilot is an assessment MVP for a government-domain,
retrieval-augmented assistant. It helps an authorized government officer turn a
citizen situation into an evidence-grounded service response: identify a service
and supported eligibility, required documents, fees, timelines, and an official
response draft. Government information is safety-critical; the system must not
invent rules, procedures, eligibility outcomes, fees, or deadlines.

The assessment domain is **D4 — Government: citizen services and regulations**.
This document separates target requirements from the implemented MVP. Detailed
controls are in [SECURITY.md](SECURITY.md), [CORPUS.md](CORPUS.md),
[evaluation.md](evaluation.md), [orchestration.md](orchestration.md),
[OBSERVABILITY.md](OBSERVABILITY.md), and [ADRs](adr/).

## Goals

- Provide an authenticated, tenant-isolated government-service workflow.
- Retrieve relevant evidence for the current tenant only.
- Generate cited answers from evidence or clearly refuse when evidence is
  insufficient; never guess government facts.
- Require a human supervisor before a consequential staged action is executed.
- Make important runs accountable without retaining secrets or unnecessary raw
  content.
- Meet the assessment corpus requirement with safe synthetic/public material.

## Stakeholders and personas

| Persona | Need | MVP interaction |
|---|---|---|
| Citizen | Accurate service information and a safe response. | Indirect beneficiary; no public citizen self-service UI is implemented. |
| Government officer | Research a situation and prepare a sourced response efficiently. | Authenticated API/CLI workflows for ingestion, search, answers, sessions, and orchestration. |
| Government supervisor | Review and authorize a consequential draft. | Supervisor-only approval decision and execution endpoints. |
| Tenant operator | Safely operate tenants, corpus, configuration, and diagnostics. | Server-side identity, tenant-scoped data access, Docker/local workflows, and trace inspection. |
| Assessor/developer | Verify safety, architecture, corpus coverage, and repeatability. | Tests, evaluation harness, synthetic corpus, and documented controls. |

## Scope and MVP boundaries

### Target scope

The target workflow accepts a citizen situation; identifies the relevant
government service and supported eligibility; retrieves tenant-authorized
evidence about documents, fees, timelines, and procedure; creates a cited
official response draft; and stages consequential action for human approval.

### Implemented MVP

The MVP provides deterministic document ingestion/chunking, PostgreSQL/pgvector
persistence, tenant-scoped hybrid retrieval and reranking, grounded answers with
citation validation and evidence-based refusal, bounded multi-agent
orchestration, staged approval, SSE progress streaming, sessions/run traces, an
evaluation harness, and a synthetic D4 corpus. It exposes API and CLI workflows.
The Angular web project is not part of the assessment Docker stack.

Gemini is the default chat provider; a configured local Ollama chat adapter is
also available without Application or Domain changes. Provider endpoints and
credentials remain server-side configuration.

### Out of scope

- Production identity-provider integration and production credential lifecycle.
- A public/anonymous citizen portal or automatic publication of advice.
- Automatic execution of consequential government actions.
- Live official-government data, real personal data, or an assertion that model
  output is an authoritative government determination.
- A bundled Ollama server/model or a required live Gemini/Ollama runtime.
- Replacing legal, policy, or supervisory review.

## Assumptions and constraints

- Authoritative tenant identity comes only from authenticated server-side
  context. Client values, including headers, bodies, queries, CLI arguments, and
  model output, cannot override it.
- Synthetic identities exist only for Development, Testing, and CI. Production
  requires a trusted identity source; see [SECURITY.md](SECURITY.md).
- Corpus material must be public or synthetic, contain no real personal data,
  and satisfy the assessment size requirement.
- LLM and embedding providers are external dependencies. Provider URLs and
  credentials are server-side only.
- Orchestration, tools, retrieval, retries, timeouts, and cancellation are
  bounded; agent/tool execution is never unbounded.
- Citation validation establishes evidence linkage, not legal or semantic proof.
  Human review remains necessary for consequential use.

## Functional requirements

| ID | Requirement | Implemented MVP status |
|---|---|---|
| FR-1 | Maintain at least 30 documents and 150+ pages of public/synthetic data, with no real personal data. | Implemented: 32 synthetic documents, 160 explicit pages, 16 per synthetic tenant; manifest validation and authenticated, idempotent seeding. See [CORPUS.md](CORPUS.md). |
| FR-2 | Answer government-service questions from retrieved evidence, with citations, and refuse when evidence is insufficient. | Implemented: grounded-answer use case, citation validation, sufficiency checks, and typed refusal. See [evaluation.md](evaluation.md) and ADR 0008. |
| FR-3 | Evaluate retrieval/answer quality reproducibly. | Implemented: golden dataset and deterministic retrieval hit rate, groundedness, and refusal-correctness measures. See [evaluation.md](evaluation.md). |
| FR-4 | Orchestrate bounded specialized steps for government research and drafting. | Implemented foundation: document search, eligibility lookup, procedure lookup, response drafting, bounded execution, and fallback. See [orchestration.md](orchestration.md). |
| FR-5 | Require human approval before a staged consequential action can execute. | Implemented: pending approvals and supervisor-only decision/execution controls. |
| FR-8 | Authenticate users, enforce RBAC, and resolve tenant identity server-side. | Implemented for development/test/CI synthetic identities and protected API endpoints. Production identity integration remains a deployment dependency. |
| FR-9 | Record safe accountability information for important runs and LLM use. | Implemented: tenant-scoped sessions/run traces, correlation/run IDs, timing, status, provider/model, and available usage metadata. See [OBSERVABILITY.md](OBSERVABILITY.md). |

## Government D4 workflow

1. Capture the citizen situation and identify the applicable government service.
2. Retrieve relevant, tenant-scoped evidence for eligibility, documents, fees,
   timelines, and procedure.
3. Produce a cited official response draft only from that evidence, or refuse or
   request authoritative clarification if evidence is insufficient.
4. Stage a consequential action as a pending approval request.
5. Allow only an authenticated supervisor to decide and execute an approved
   request.

The MVP is an officer-facing orchestration and grounded-answer flow. It does
not autonomously publish advice or complete a government transaction. Agent
roles, tool allow-lists, fallback behavior, and streaming events are documented
in [orchestration.md](orchestration.md).

## T0 multi-tenancy

T0 is a security boundary, not a display feature. Every tenant-scoped read,
write, vector/keyword query, session, approval, and trace must use the
authenticated server-side tenant. Cross-tenant access must fail. The MVP has two
synthetic tenants and isolation tests; corpus seeding selects documents only for
the authenticated tenant and rejects `--tenant-id` overrides.

## Approval, grounding, security, and privacy

Consequential actions must be staged and remain pending until a human decision.
Only an authenticated supervisor can decide and execute an approved request.
Cancellation, failure, and refusal cannot bypass this boundary.

The system treats ingested/retrieved documents as untrusted data, preserves
system/application instructions over document content, and uses bounded,
tenant-scoped evidence. Grounded answers require validated citations. When
evidence is inadequate or citations cannot be validated, the system must refuse
rather than invent facts.

Protected operations require authentication/authorization. Secrets, API keys,
authorization headers, prompts, raw evidence, and arbitrary upstream provider
response bodies must not be committed, exposed, or logged. See
[SECURITY.md](SECURITY.md) for the implemented controls and threat model.

## Non-functional requirements

| Area | Requirement |
|---|---|
| Safety | Bounded retries/iterations, provider timeouts, cancellation, and safe fallback where specified. |
| Reliability | Deterministic chunking, idempotent corpus re-ingestion, and reproducible Docker migration/seed workflow. |
| Performance | Bounded, tenant-scoped vector and keyword retrieval with deterministic reranking and no global candidate pool. |
| Accountability | Correlation/run IDs; tenant-scoped sessions/traces; status, timing, provider/model, evidence references, and available token/cost information. |
| Privacy | Synthetic assessment corpus only; no real personal data; minimize retained content and exclude secrets. |
| Maintainability | Clean Architecture dependencies, adapters behind abstractions, preserved migrations, and meaningful tests. |
| Configuration | Gemini is the default chat provider; local Ollama is selectable only by server-side configuration and requires no API key. |

## Acceptance criteria

- Corpus validation confirms at least 30 documents and 150+ explicit pages; the
  committed corpus currently validates 32 documents and 160 pages.
- Each synthetic tenant can seed with its authenticated identity, remains
  limited to 16 assigned documents, and cannot override the tenant.
- A supported tenant-scoped query returns a grounded answer with valid
  citations; unsupported/out-of-corpus queries use the refusal path.
- The evaluation harness produces its documented deterministic metrics.
- Orchestration stages approval and cannot execute without a supervisor decision.
- Streaming emits progress/answer events incrementally and cancellation prevents
  automatic consequential execution.
- Build, relevant automated tests, focused workflow checks, and `git diff
  --check` pass in the available environment; unavailable external dependencies
  are reported rather than masked.
- Gemini remains the default adapter. Selecting Ollama through configuration
  changes only the infrastructure adapter and preserves the Application/Domain
  grounded-answer workflow, as covered by deterministic adapter/acceptance tests.

## Current MVP boundaries and follow-on decisions

This BRD documents the assessment target and current MVP; it is not a claim of
production readiness. Synthetic identities, synthetic corpus content, optional
model servers, and the absence of production identity/data governance remain
explicit boundaries. Any future expansion of authority, automation, provider
exposure, data sources, or tenant controls requires a documented requirements
and architecture decision before implementation.
