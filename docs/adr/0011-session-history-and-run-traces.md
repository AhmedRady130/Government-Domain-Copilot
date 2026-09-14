# 11. Durable Session History and Orchestration Run Traces

Date: 2026-09-14
Status: Accepted

## Context

FR-7 requires comprehensive client access (API and CLI) to:
- Document Ingestion
- Evidence-Grounded Answering
- Multi-Agent Orchestration Runs
- Human-in-the-Loop Approvals
- Orchestration Run Telemetry / Traces
- Persistent Conversation Session History

Earlier iterations utilized in-memory dictionary stores (`InMemorySessionStore` and `InMemoryRunTraceStore`) to prototype data models and unit tests. However, an architectural audit established that ephemeral in-memory storage:
1. Does not survive application process restarts or deployments.
2. Cannot support multi-pod / horizontal cluster scaling in production.
3. Violates the explicit requirement for "Persistent session history".

## Decision

We replace the in-memory runtime stores with durable, relational PostgreSQL persistence backed by EF Core:
1. **Entity Models in Infrastructure Layer (`src/Infrastructure/Persistence/Entities`)**:
   - `ConversationSessionEntity` (`UserSessions` table): Tracks session metadata, title, created timestamp, last activity timestamp, and status.
   - `SessionMessageEntity` (`SessionMessages` table): Persists ordered conversation messages, role, content, status, structured citations JSON, linked run ID, and timestamp.
   - `OrchestrationRunTraceEntity` (`OrchestrationRunTraces` table): Stores complete execution traces, correlation ID, pattern name, timings, iterations, fallback outcomes, agent executions JSON, pending approvals JSON, and final answer metadata.
2. **Database Integration**:
   - Mapped into the existing `GovernmentDomainCopilotDbContext` to preserve single-context transactional integrity.
   - Configured with strict composite indexes (`TenantId + SessionId`, `TenantId + LastActivityAt`, `TenantId + StartedAt`, `SessionId + Timestamp`).
3. **Store Implementations (`src/Infrastructure/Sessions` & `src/Infrastructure/Traces`)**:
   - `PostgresSessionStore` implements `ISessionStore`.
   - `PostgresRunTraceStore` implements `IRunTraceStore`.
   - Strict tenant scoping enforced on every query and mutation via parameterized EF Core queries (`s.TenantId == tenantId`).
4. **Dependency Injection**:
   - Production API and CLI resolve `ISessionStore -> PostgresSessionStore` and `IRunTraceStore -> PostgresRunTraceStore`.
   - In-memory stores remain available in `Application` solely for lightweight unit/stub testing.
5. **Data Protection Invariants**:
   - Only finalized, validated assistant responses are stored (no partial streaming chunks).
   - Maximum content lengths are strictly validated before write.
   - No API keys, authorization headers, system prompts, raw chunk documents, or secrets are ever persisted.

## Consequences

### Positive
- True durability across server restarts, updates, and host migrations.
- Fully compatible with multi-replica Kubernetes / container cluster deployments.
- Full auditability and correlation between user queries, multi-agent iterations, and grounded answer citations.
- Clean Architecture preserved: domain models and application abstractions have zero dependencies on EF Core or Npgsql.

### Negative / Trade-offs
- Database round-trips for session and trace writes (mitigated through async I/O and indexed lookups).
- Database migrations must be managed and applied across environments.

## Compliance
- Addresses FR-7 requirement for durable session history and run trace inspection.
- Enforces multi-tenancy security boundaries with database-level isolation.
