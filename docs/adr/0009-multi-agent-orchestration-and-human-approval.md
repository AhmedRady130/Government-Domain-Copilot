# ADR 0009: Multi-Agent Orchestration Foundation and Human-in-the-Loop Approval Boundary

## Status

Accepted

## Context

Previous PRs implemented deterministic document ingestion, pgvector storage, hybrid search (RRF), deterministic reranking, grounded answer generation with citations, refusal behavior, and evaluation harness.
To satisfy FR-4 and FR-5 of the Government Domain Copilot, complex citizen inquiries requiring structured analysis of statutory eligibility, procedural workflows, and verified drafting must be handled by a multi-agent orchestration architecture while maintaining strict government safety invariants:
1. **Clean Architecture & Decoupling**: Application layer abstractions must remain framework-agnostic and free of direct dependencies on LLM provider SDKs, HTTP, EF Core, or web frameworks.
2. **Explicit Agent Contracts & Tool Allow-Lists**: Every agent must have an explicit role, allowed tools, input/output contract, and termination condition. Agents must never invoke tools outside their allow-list.
3. **Deterministic Orchestration & Safety Limits**: The orchestration pattern must enforce maximum iterations, timeouts, bounded retries with exponential backoff, deterministic failure handling, and caller cancellation.
4. **Human Approval Boundary for Side Effects**: Side-effecting operations (e.g. publishing official government advisories) must never execute automatically. They must stage an explicit `ApprovalRequest` with `Pending` status and remain blocked from consequential execution until an authenticated human supervisor explicitly approves or edits the request.
5. **Plain-RAG Fallback**: If multi-agent orchestration fails (e.g., retry exhaustion, iteration limit, timeout), it must safely fall back to the proven deterministic grounded-answer plain-RAG pipeline (`GroundedAnswerUseCase`), preserving tenant isolation, evidence sufficiency, citation validation, and refusal behavior.
6. **Strict Multi-Tenancy**: Tenant context must be derived strictly from server-side `ITenantContext`. Caller-supplied tenant IDs must never be trusted.

## Decision

1. **Framework-Agnostic Abstractions (`Application/Agents/Abstractions`)**:
   - `IAgent`: defines agent contracts (`Role`, `Description`, `AllowedToolNames`, `InputContract`, `OutputContract`, `TerminationCondition`, `ExecuteAsync`).
   - `IAgentTool`: defines tool execution (`Name`, `Description`, `IsSideEffecting`, `ExecuteAsync`).
   - `IApprovalManager`: manages human approval lifecycle (`CreateRequestAsync`, `GetRequestAsync`, `SubmitDecisionAsync`, `ExecuteActionAsync`).
   - `IMultiAgentOrchestrator`: coordinates the execution pipeline and records inspectable run traces (`PatternName`, `OrchestrateAsync`).

2. **Specialized Government Domain Agents**:
   - **Eligibility Identifier Agent**: Analyzes citizen criteria, prerequisites, exemptions, and disqualifications. Allowed tools: `eligibility_lookup`, `document_search`.
   - **Procedure Resolver Agent**: Resolves workflows, required forms, statutory deadlines, submission venues, and government fees. Allowed tools: `procedure_lookup`, `document_search`.
   - **Response Drafter Agent**: Synthesizes upstream findings into a verified government response with exact citations using `IGroundedAnswerUseCase`, then invokes `create_draft_approval` to stage the draft for human supervisor review.

3. **Tools & Side-Effecting Protection**:
   - `DocumentSearchTool`: Hybrid vector and keyword retrieval. Read-only (`IsSideEffecting = false`). Content excerpts in trace logs are bounded to prevent raw document dumps.
   - `EligibilityLookupTool`: Extracts eligibility criteria. Read-only (`IsSideEffecting = false`).
   - `ProcedureLookupTool`: Extracts procedural steps and fee schedules. Read-only (`IsSideEffecting = false`).
   - `DraftApprovalTool`: Staging-only side-effecting tool (`IsSideEffecting = true`). Creates a `Pending` `ApprovalRequest`. Consequential execution is strictly blocked before approval.

4. **Named Orchestration Pattern: "Sequential Pipeline with Human-in-the-Loop & Plain-RAG Fallback"**:
   - **Sequence**: `EligibilityIdentifierAgent` → `ProcedureResolverAgent` → `ResponseDrafterAgent`.
   - **Handoff Data**: `AgentContext` state dictionary passes `EligibilityEvaluation`, `ProcedureWorkflow`, `GroundedAnswerResponse`, and `PendingApprovalRequest`.
   - **Tool Scoping**: Prior to agent execution, the orchestrator filters available tools strictly against the agent's `AllowedToolNames`.
   - **Safety Controls**:
     - `MaxIterations`: Enforces loop termination.
     - `TimeoutSeconds`: Enforces linked token timeout.
     - `MaxRetries` & `RetryBackoffMilliseconds`: Bounded retries with exponential backoff for transient failures.
     - `CancellationToken`: Immediate cancellation propagation without triggering fallback when explicitly cancelled by the caller.

5. **Human Approval Boundary (`IApprovalManager`)**:
   - Supported decisions: `Approve`, `Reject`, `Edit`.
   - Guarded execution: `ExecuteActionAsync` and `ApprovalRequest.MarkExecuted` throw `InvalidOperationException` if the request status is `Pending` or `Rejected`.
   - Staged execution: In PR #20, approved execution safely stages and records execution metadata without mutating real-world external government systems.

6. **Plain-RAG Fallback Invariant**:
   - Triggered upon unrecoverable orchestration failure when `EnableFallback` is true.
   - Reuses `IGroundedAnswerUseCase`, ensuring evidence sufficiency evaluation, exact citation validation, prompt injection defense, and typed refusal.
   - Preserves tenant isolation via `ITenantContext`.
   - Does not bypass approval requirements.

7. **Inspectable Run Model (`OrchestrationRunRecord`)**:
   - Emits structured run records containing `RunId`, `CorrelationId`, `TenantId`, `PatternName`, `IterationCount`, `Status`, timings, agent executions, tool calls, fallback indicators, and pending approval state.
   - Strictly excludes secrets, API keys, authorization headers, and unnecessary full document text.

## Consequences

- **Positive**: Modular, decoupled multi-agent foundation satisfying D4 government workflows; strict human approval boundary preventing unauthorized consequential actions; deterministic safety controls and plain-RAG fallback.
- **Negative**: Adds sequential latency across 3 specialized agent steps; approval workflow requires supervisory human review before actions can proceed.
