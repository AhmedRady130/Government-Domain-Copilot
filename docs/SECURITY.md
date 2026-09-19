# Government Domain Copilot — Security Documentation

> **Scope:** FR-8 Authentication, Role-Based Access Control, and Tenant Identity  
> **Branch:** PR #29 security and governance worktree
> **OWASP references:** OWASP Top 10 (A01 Broken Access Control, A07 Identification and Authentication Failures), OWASP LLM Top 10 (LLM09 Misinformation, LLM02 Sensitive Information Disclosure, LLM06 Excessive Agency)

---

## 1. Authentication Architecture

### Mechanism
Government Domain Copilot implements real server-side authentication using an ASP.NET Core `AuthenticationHandler` (`ApiKeyAuthenticationHandler`).

Supported HTTP credentials:

| Header | Format | Notes |
|--------|--------|-------|
| `X-API-Key` | `<key>` | Custom header |
| `Authorization` | `Bearer <key>` | Standard Bearer format |
| `Authorization` | `ApiKey <key>` | Alternate scheme prefix |

The handler resolves keys in order of precedence: `X-API-Key` → `Authorization: Bearer` → `Authorization: ApiKey`.

### Production vs. Development / Test Boundaries

> [!IMPORTANT]
> **Production vs. Development Boundary**:
> - `SeedAuthIdentities` and deterministic synthetic API keys (`gov-key-*`) are strictly isolated to **Development**, **Testing**, and **CI** environments.
> - The authentication handler enforces an explicit server-side environment guard: when `IHostEnvironment.IsDevelopment()` is `false`, any attempt to authenticate using synthetic seed identities is **immediately rejected and failed closed**.
> - **Production Deployment Dependency**: Production deployments require configuring a trusted external identity source (such as enterprise OpenID Connect / OAuth2 JWT bearer tokens or a cryptographically hashed production API key store). Seeded keys are never production-grade credentials.

### Authentication Handler Failure Modes:
- **No credentials supplied**: Returns `AuthenticateResult.NoResult()`, allowing the framework challenge to return `401 Unauthorized`.
- **Invalid or unrecognized credentials**: Returns `AuthenticateResult.Fail()`, resulting in `401 Unauthorized`.
- **Production environment with seeded identities**: Returns `AuthenticateResult.Fail()`, failing closed with `401 Unauthorized`.

---

## 2. Claims-Based Tenant Identity and Fail-Closed Tenancy

### Platform Claims
On successful authentication, the handler constructs a `ClaimsPrincipal` containing authoritative identity claims:

| Claim Type | Meaning | Value Example |
|------------|---------|---------------|
| `ClaimTypes.NameIdentifier` | Subject / User ID | `aaaa0001-0000-0000-0000-000000000001` |
| `"sub"` | Subject ID | `aaaa0001-0000-0000-0000-000000000001` |
| `"tenant_id"` | Server-Side Tenant ID | `11111111-1111-1111-1111-111111111111` |
| `ClaimTypes.Name` | User Display Name | `Tenant A Officer` |
| `"external_id"` | External Identity Reference | `officer-a` |
| `ClaimTypes.Role` | Role Name | `Officer` or `Supervisor` |

### Application Identity Abstraction (`ICurrentUserContext`)
Application code depends exclusively on `ICurrentUserContext` (`src/Application/Abstractions/ICurrentUserContext.cs`) and `ITenantContext`. The Application layer has **zero dependencies** on ASP.NET Core `HttpContext`, `ClaimsPrincipal`, or web framework assemblies, preserving Clean Architecture.

### Tenant Resolution and Fail-Closed Behavior
- **In Production**: `ITenantContext` is mapped directly to `CurrentUserContext`. It extracts `tenant_id` from authenticated claims. If unauthenticated, the request was rejected with 401 at the endpoint level. If authenticated but missing the `tenant_id` claim, `CurrentUserContext.GetTenantId()` throws an `InvalidOperationException`, and the endpoint responds with `403 Forbidden`. **No default tenant is ever used in Production**.
- **In Development/Testing**: When unauthenticated, `DevelopmentTenantContext` provides a fallback to configured local development tenant. When authenticated, it extracts tenant identity from the authenticated claims.
- **Client Spoofing Blocked**: `X-Tenant-ID` headers, request body `tenantId` fields, and query parameters are **never read for authorization**. They are completely ignored.

---

## 3. Role-Based Access Control (RBAC)

Two roles are defined in `GovernmentDomainCopilot.Domain.Constants.Roles`:

| Role | Permitted Actions |
|------|-------------------|
| **`Officer`** | Ingest documents, execute searches, generate grounded answers, run multi-agent orchestration, view sessions and session messages, view own tenant runs and traces, inspect pending approval requests. |
| **`Supervisor`** | All `Officer` capabilities, **plus** submit human-in-the-loop approval decisions (`POST /api/approvals/{id}/decide`), and execute approved staging actions (`POST /api/approvals/{id}/execute`). |

### Server-Side Authorization Matrix

| Endpoint | Method | Required Policy / Role | Unauthenticated | Officer | Supervisor |
|----------|--------|------------------------|-----------------|---------|------------|
| `/health` | GET | Anonymous | 200 OK | 200 OK | 200 OK |
| `/api/documents` | POST | Authenticated | 401 | 201 Created | 201 Created |
| `/api/documents/{id}` | GET | Authenticated | 401 | 200 OK | 200 OK |
| `/api/search` | GET | Authenticated | 401 | 200 OK | 200 OK |
| `/api/answer` | POST | Authenticated | 401 | 200 OK | 200 OK |
| `/api/orchestrate` | POST | Authenticated | 401 | 200 OK | 200 OK |
| `/api/orchestrate/stream` | POST | Authenticated | 401 | 200 OK (SSE) | 200 OK (SSE) |
| `/api/runs` | GET | Authenticated | 401 | 200 OK | 200 OK |
| `/api/runs/{id}` | GET | Authenticated | 401 | 200 OK | 200 OK |
| `/api/sessions` | POST / GET | Authenticated | 401 | 200 / 201 | 200 / 201 |
| `/api/sessions/{id}` | GET | Authenticated | 401 | 200 OK | 200 OK |
| `/api/sessions/{id}/messages` | GET / POST | Authenticated | 401 | 200 OK | 200 OK |
| `/api/approvals` | GET | Authenticated | 401 | 200 OK | 200 OK |
| `/api/approvals/{id}` | GET | Authenticated | 401 | 200 OK | 200 OK |
| `/api/approvals/{id}/decide` | POST | **`SupervisorOnly`** | 401 | **403 Forbidden** | 200 OK |
| `/api/approvals/{id}/execute` | POST | **`SupervisorOnly`** | 401 | **403 Forbidden** | 200 OK |

---

## 4. Synthetic Development/Test Identities (T0)

To support automated CI and contract testing across >= 2 isolated tenants without using real credentials or PII:

| Identity Key | Tenant ID | Role | API Key |
|--------------|-----------|------|---------|
| `TenantAOfficer` | `11111111-1111-1111-1111-111111111111` | `Officer` | `gov-key-tenant-a-officer` |
| `TenantASupervisor` | `11111111-1111-1111-1111-111111111111` | `Supervisor` | `gov-key-tenant-a-supervisor` |
| `TenantBOfficer` | `22222222-2222-2222-2222-222222222222` | `Officer` | `gov-key-tenant-b-officer` |
| `TenantBSupervisor` | `22222222-2222-2222-2222-222222222222` | `Supervisor` | `gov-key-tenant-b-supervisor` |

---

## 5. Client CLI Security Controls

- **No Tenant Spoofing**: The CLI strictly rejects any `--tenant-id` flag at argument validation time.
- **Identity Propagation**: When invoked with `--api-key <key>`, the CLI maps the authenticated user and tenant into DI, binding `ITenantContext` and `ICurrentUserContext` directly to that authenticated identity.
- **Server-Side CLI Role Enforcement**: Commands requiring supervisor privileges (`approve`, `reject`, `execute-approval`) verify the caller's role against `Roles.Supervisor`. Non-supervisors receive an `Access Denied` error and exit code 1.
- **Safe Secrets Handling**: API keys are never echoed to console logs or written into files.

---

## 6. Threat Mitigations and OWASP LLM Alignment

| Identified Threat | Security Control |
|-------------------|------------------|
| **Unauthenticated Access (OWASP A07)** | All data and execution endpoints enforce `.RequireAuthorization()`. Unauthenticated requests return `401 Unauthorized`. |
| **Privilege Escalation (OWASP A01)** | `SupervisorOnly` authorization policy protects `/api/approvals/{id}/decide` and `/execute`. Officers receive `403 Forbidden`. |
| **Cross-Tenant Data Leakage** | All database queries, sessions, traces, and vector searches are strictly filtered by `ITenantContext.GetTenantId()`. Tenant A credentials cannot access Tenant B resources. |
| **Tenant Header Spoofing** | Custom headers (e.g. `X-Tenant-ID`) are never trusted for authorization; tenant ID is sourced exclusively from authenticated claims. |
| **Excessive Agency (OWASP LLM06)** | Human-in-the-loop approval gate stages consequential actions. Staged actions cannot execute while in `Pending` or `Rejected` states, and execution is strictly limited to Supervisors. |
| **Sensitive Information Disclosure (OWASP LLM02)** | Provider API keys, credentials, and internal stack traces are redacted from responses and telemetry. |
| **Prompt Injection (OWASP LLM01)** | Ingested documents are treated strictly as untrusted data, never as system instructions. |
| **Prompt/PII telemetry exposure** | Eligibility and procedure agent logs retain only query presence and character count. Their persisted lookup-tool diagnostics retain metadata and stable failure codes only, not raw prompts, retrieved evidence, credentials, or upstream exception text. |
| **Request exhaustion** | A centralized API user-text validator applies the server-configured `ApiSecurity:MaxQueryLength` to answer, search, orchestration (including SSE), and session-message requests. Request-body limits and tenant/IP-partitioned rate limits bound request volume. Rejections return stable error codes; `429` responses include a positive `Retry-After` value. |
| **Exception-message disclosure** | API endpoints, Application use cases, and provider adapters use `src/Application/Observability/SafeExceptionLoggingExtensions.cs`. It logs only error code, operation, exception type, correlation ID, and duration; it does not pass exception messages, prompts, evidence, stack traces, or credentials to log providers. Provider adapters also avoid placing upstream response bodies in thrown exception messages. |

### Implemented endpoint protections

`/api/answer`, `/api/orchestrate`, `/api/orchestrate/stream`, and `/api/sessions/{sessionId}/messages` require the AI-workload limiter. `/api/search` uses the same bounded policy because it triggers embedding/retrieval work. All routes are also covered by a tenant-or-IP-partitioned global limiter; document ingestion is therefore protected even though it is not an AI workload. Limits are read only from server configuration.

### Document-ingestion input boundary

`POST /api/documents` accepts JSON text (`title`, `sourceReference`, and `sourceText`), not multipart files. It rejects every non-JSON request, including multipart bodies, with the stable `UnsupportedDocumentMediaType` error before file metadata, filenames, or contents are parsed. Consequently, filename sanitization, extension/content-type allow-lists, magic-byte validation, and generated file-storage names are not applicable: this endpoint neither accepts nor stores files. Its request body is bounded by server-side `ApiSecurity:MaxRequestBodyBytes` and returns `RequestBodyTooLarge` on the middleware rejection path; Application validation rejects empty text and source text longer than 500,000 characters before chunking. Binary-file ingestion requires a separately designed endpoint and storage threat model.

### PII redaction at ingestion

Before `sourceText` reaches the ingestion use case or persistence, `IPiiRedactor` replaces detected email addresses, phone numbers, and 14-digit national IDs with typed redaction markers. The endpoint logs only category counts (`EmailCount`, `PhoneNumberCount`, and `NationalIdCount`), never matched values or raw source text. This deterministic regex control is a baseline, not a replacement for a jurisdiction-specific data-classification, retention, consent, or human-review program.

### Browser-facing HTTP controls

`SecurityHeadersMiddleware` emits a restrictive API CSP (`default-src 'none'`), `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, and `Referrer-Policy: strict-origin-when-cross-origin`. HSTS (`max-age=31536000; includeSubDomains`) is emitted only for HTTPS requests, as required for meaningful browser enforcement. CORS uses the explicit `ApiSecurity:AllowedOrigins` configuration list and permits methods/headers only for listed origins; it does not use wildcard origins or credentialed CORS. An empty list denies cross-origin browser access.

### Tenant isolation boundary and RLS roadmap

The current enforcement layer is Application/Infrastructure: authenticated tenant identity is resolved server-side, every repository/retrieval query predicates on `TenantId`, tenant-aware compound keys protect relationships, and PostgreSQL integration tests prove relational, keyword, vector, embedding-write, and grounded-answer cross-tenant isolation. This protects normal application paths today while preserving Clean Architecture.

PostgreSQL RLS is a separate database-layer defense-in-depth control and is **not yet implemented**. It is intentionally tracked as a future mitigation in the [SYSTEM-DESIGN gap table](SYSTEM-DESIGN.md); its PR must set a trusted transaction-scoped tenant context, add policies for every tenant-owned table, and prove that direct database access cannot bypass tenant isolation.

### OWASP coverage and known boundaries

| Threat category | Implemented control or current boundary |
|---|---|
| OWASP Web A01 Broken Access Control | Authentication, role policies, and tenant-scoped application/repository operations are implemented. PostgreSQL RLS/data-layer enforcement is **not** implemented in this PR and is planned separately. |
| A02 Cryptographic Failures | Secrets are read from configuration/environment and are excluded from error responses/logging; key storage, rotation, and transport deployment controls are operational responsibilities not implemented here. |
| A03 Injection | Parameterized persistence access and request validation are used; no claim is made that arbitrary document content is safe to execute. |
| A04 Insecure Design | Clean Architecture boundaries, explicit approval gates, bounded retries/iterations, and typed endpoint contracts are implemented. |
| A05 Security Misconfiguration | Production seed-identity guard and least-privilege workflow permissions are implemented; environment hardening remains deployment-owned. |
| A06 Vulnerable and Outdated Components | Dependabot and security workflows are present; unresolved test-only transitive advisories are tracked separately in this PR report. |
| A07 Identification and Authentication Failures | Server-side API-key authentication and fail-closed production behavior are implemented; a production external identity source remains required. |
| A08 Software and Data Integrity Failures | CI dependency/secret scans are configured; third-party action SHA pinning is pending independently verified SHAs. |
| A09 Security Logging and Monitoring Failures | Correlation IDs and safe failure metadata are recorded without exception-content leakage. |
| A10 Server-Side Request Forgery | Provider base URLs are server configuration, not API request fields; outbound allow-listing is not implemented. |
| OWASP LLM01 Prompt Injection | Retrieved/ingested text is treated as untrusted data and cannot override application instructions; direct and indirect injection regression tests cover grounded-answer behavior. |
| LLM02 Sensitive Information Disclosure | Grounded responses require citations, and API/provider failure paths avoid secrets, prompts, evidence, and exception text. |
| LLM03 Supply Chain | Dependency scanning and Dependabot are implemented; action SHA pinning and production dependency governance remain pending. |
| LLM04 Data and Model Poisoning | Tenant-scoped ingestion/retrieval and evidence grounding reduce exposure; corpus governance and provenance review are not implemented controls. |
| LLM05 Improper Output Handling | Grounded/refusal responses are returned as data; downstream execution is blocked by approval boundaries. |
| LLM06 Excessive Agency | Consequential actions require supervisor authorization and explicit human approval before execution. |
| LLM07 System Prompt Leakage | Prompt content is not exposed in safe API errors or safe failure logs; this is not a guarantee against all model-output leakage. |
| LLM08 Vector and Embedding Weaknesses | Retrieval is tenant-scoped in application/repository paths; PostgreSQL RLS is explicitly out of scope for this PR. |
| LLM09 Misinformation | Retrieval evidence, citation validation, and refusal on insufficient evidence are implemented. |
| LLM10 Unbounded Consumption | Server-side body/query limits, AI rate limits, and bounded orchestration retries/iterations are implemented. |

---

*Document revised for PR #29 security and governance work.*
