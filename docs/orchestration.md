# Multi-Agent Orchestration & Human Approval Foundation

This document details the Multi-Agent Orchestration Foundation (FR-4) and Human-in-the-Loop Approval Boundary (FR-5) implemented in the Government Domain Copilot.

---

## 1. Named Orchestration Pattern

**Pattern Name**: `Sequential Pipeline with Human-in-the-Loop & Plain-RAG Fallback`

The orchestrator executes a sequence of specialized agents to process complex citizen inquiries according to government statutory workflows:

```
                            User Question
                                  │
                                  ▼
                   ┌──────────────────────────────┐
                   │ Eligibility Identifier Agent │
                   │ (Allowed: eligibility_lookup,│
                   │           document_search)   │
                   └──────────────┬───────────────┘
                                  │ State: EligibilityEvaluation
                                  ▼
                   ┌──────────────────────────────┐
                   │  Procedure Resolver Agent    │
                   │ (Allowed: procedure_lookup,  │
                   │           document_search)   │
                   └──────────────┬───────────────┘
                                  │ State: ProcedureWorkflow
                                  ▼
                   ┌──────────────────────────────┐
                   │    Response Drafter Agent    │
                   │ (Allowed: create_draft_      │
                   │           approval)          │
                   └──────────────┬───────────────┘
                                  │
                  ┌───────────────┴───────────────┐
                  ▼                               ▼
       (Sufficient Evidence)            (Refusal / Insufficient)
                  │                               │
                  ▼                               ▼
       ┌─────────────────────┐          ┌───────────────────┐
       │ Create Approval Req │          │ Terminate Early   │
       │ (Status: Pending)   │          │ (Status: Refused) │
       └──────────┬──────────┘          └───────────────────┘
                  │
   ┌──────────────┼──────────────┐
   ▼              ▼              ▼
[Approve]      [Reject]        [Edit]
   │              │              │
   ▼              ▼              ▼
Executable     Blocked        Executable
(Staged)       (Error)        (Staged)
```

### Participating Agents

| Agent Name | Primary Responsibility | Allowed Tools | Input Contract | Output Contract | Termination Condition |
| :--- | :--- | :--- | :--- | :--- | :--- |
| **Eligibility Identifier Agent** | Identifies applicant qualification criteria, prerequisite conditions, exemptions, and disqualifications. | `eligibility_lookup`, `document_search` | `AgentContext` containing citizen query in `UserQuery`. | `AgentExecutionResult` with evaluated criteria stored under state key `'EligibilityEvaluation'`. | Terminates when eligibility requirements and prerequisite conditions are extracted and recorded in state, or evidence indicates non-applicability. |
| **Procedure Resolver Agent** | Resolves procedural workflows, required documents, statutory deadlines, submission venues, and government fees. | `procedure_lookup`, `document_search` | `AgentContext` with `UserQuery` and optional upstream `'EligibilityEvaluation'` state. | `AgentExecutionResult` with resolved procedure steps, forms, fees, and deadlines stored under state key `'ProcedureWorkflow'`. | Terminates when statutory procedural workflow, fees, and timelines are fully resolved and recorded in state. |
| **Response Drafter Agent** | Synthesizes upstream findings into a grounded response with exact citations via `IGroundedAnswerUseCase`, then queues human approval. | `create_draft_approval` | `AgentContext` with `UserQuery`, `'EligibilityEvaluation'`, and `'ProcedureWorkflow'`. | `AgentExecutionResult` with grounded response, verified citations, and pending supervisor approval request. | Terminates when grounded response is synthesized, citations are validated, and the human supervisor approval request has been queued. |

---

## 2. Tool Allow-Lists and Execution

Every agent is bound to an explicit tool allow-list. Before calling an agent, the orchestrator filters all registered tools so the agent only receives tools in its allow-list:

| Tool Name | Type | Description |
| :--- | :--- | :--- |
| `document_search` | Read-only (`IsSideEffecting = false`) | Hybrid vector & keyword search over tenant's ingested government documents. Truncates log excerpts to prevent full document dumps. |
| `eligibility_lookup` | Read-only (`IsSideEffecting = false`) | Extracts and evaluates eligibility criteria from tenant-scoped regulations. |
| `procedure_lookup` | Read-only (`IsSideEffecting = false`) | Resolves procedural steps, filing timelines, and statutory fees. |
| `create_draft_approval` | Side-effecting (`IsSideEffecting = true`) | Staging tool that queues a draft response for supervisor review. **Does not execute consequential side-effects automatically.** |

---

## 3. Human Approval Boundary (FR-5)

Consequential side-effects (e.g. publishing official advisories, mutating government databases, issuing formal determinations) cannot proceed autonomously:

1. **Request Staging**: `DraftApprovalTool` creates an `ApprovalRequest` with status `ApprovalDecision.Pending`.
2. **Review Decisions**:
   - `Approve`: Human supervisor accepts the draft as written.
   - `Reject`: Human supervisor rejects the proposed action with a required reason.
   - `Edit`: Human supervisor edits the payload (e.g., correcting fee figures or legal wording) before approving.
3. **Execution Guard**:
   - Calling `ExecuteActionAsync` on a `Pending` or `Rejected` request throws an `InvalidOperationException`.
   - Only `Approved` or `Edited` requests can be executed.
   - Re-executing an already-executed action is blocked.
   - In PR #20, approved execution safely stages and records execution metadata without mutating real-world external government systems.

---

## 4. Safety Controls: Limits, Retries, and Fallback

All safety limits are configurable via `OrchestrationOptions`:

```json
"Orchestration": {
  "MaxIterations": 5,
  "TimeoutSeconds": 30,
  "MaxRetries": 2,
  "RetryBackoffMilliseconds": 50,
  "EnableFallback": true
}
```

- **Max Iterations**: Bounds total pipeline iterations. Exceeding this limit aborts the multi-agent execution safely.
- **Timeout**: Enforced via linked `CancellationTokenSource` with `TimeoutSeconds`.
- **Bounded Retries with Backoff**: Transient agent failures retry up to `MaxRetries` times using exponential/linear backoff (`backoffMs * attempt`).
- **Cancellation**: If the caller cancels via `CancellationToken`, orchestration aborts immediately and **never invokes Plain-RAG fallback**.
- **Plain-RAG Fallback**:
  - When orchestration encounters an unrecoverable failure (and caller did not cancel), it falls back to `IGroundedAnswerUseCase.GetGroundedAnswerAsync(...)`.
  - Fallback preserves tenant isolation, evidence sufficiency evaluation, exact citations, prompt injection defenses, and refusal behavior.
  - Fallback never executes unapproved consequential side-effects.

---

## 5. Tenant Isolation Boundary

- Tenant ID is resolved exclusively via `ITenantContext.GetTenantId()` from authenticated server-side context.
- Caller-supplied tenant IDs in JSON request bodies or query strings are strictly ignored.
- Cross-tenant retrieval, approval queries, decision submissions, and execution attempts fail with `KeyNotFoundException` or `null`.

---

## 6. Inspectable Run Model

Each orchestration run produces an `OrchestrationRunRecord` containing:
- `RunId`: Unique execution identifier.
- `CorrelationId`: Trace correlation identifier.
- `TenantId`: Scoped tenant identifier.
- `PatternName`: Named orchestration pattern.
- `StartedAt` / `CompletedAt` / `Duration`: Timing metrics.
- `Status`: `"Completed"`, `"CompletedWithFallback"`, `"Refused"`, `"Cancelled"`, or `"Failed"`.
- `IterationCount`: Total iterations executed.
- `AgentExecutions`: Per-agent execution telemetry, tool call counts, and outputs.
- `UsedFallback` & `FallbackReason`: Plain-RAG fallback diagnostics.
- `PendingApproval`: Attached approval request if side-effecting draft was queued.
- `FailureReason`: Error message if pipeline failed.

**Privacy & Security Invariant**: Secrets, API keys, authorization headers, and raw full document bodies are never included in run records or log output.

---

## 7. Streaming Progress Events and Cancellation

### Endpoint
`POST /api/orchestrate/stream` delivers Server-Sent Events (`text/event-stream; charset=utf-8`) with keep-alive and zero buffering (`X-Accel-Buffering: no`).

### Event Lifecycle and Ordering
1. **`RunStarted`**: Emitted once orchestration begins, containing `RunId`, `CorrelationId`, and server-authenticated `TenantId`.
2. **`AgentStarted`**: Emitted when each specialized agent in the pipeline initiates analysis.
3. **`ToolStarted` / `ToolCompleted`**: Emitted during tool execution (`DocumentSearchTool`, `EligibilityLookupTool`, `ProcedureLookupTool`, `DraftApprovalTool`).
4. **`AnswerChunk`**: Emitted incrementally as partial token deltas arrive from the configured Gemini or Ollama chat-completion provider.
5. **`ApprovalRequired`**: Emitted when `DraftApprovalTool` queues a pending approval request for human supervisor review.
6. **`FallbackStarted`**: Emitted if orchestration fails and Plain-RAG fallback is triggered.
7. **Terminal Events (Guaranteed Exactly One)**:
   - **`RunCompleted`**: Emitted ONLY when the full answer passes structural citation validation and grounding policies.
   - **`RunFailed`**: Emitted when unrecoverable failure occurs, or when citation validation fails/refusal is triggered.
   - **`RunCancelled`**: Emitted when the caller cancels the operation.

### Cancellation Invariants
- **Responsive Cancellation**: `CancellationToken` propagates through every asynchronous step.
- **No Fallback on Cancellation**: Explicit caller cancellation immediately terminates execution and **never** triggers Plain-RAG fallback.
- **No Side-Effect Execution**: Consequential actions and tool executions are aborted; any staged approval requests remain in `Pending` status.
- **Strict Multi-Tenancy**: The streaming endpoint resolves tenant identity strictly via `ITenantContext`. Any client-supplied tenant overrides (e.g. `X-Tenant-ID` header) are strictly ignored.
