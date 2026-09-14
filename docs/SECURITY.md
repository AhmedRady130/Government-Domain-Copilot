# Government Domain Copilot — Security Documentation

> **Scope:** FR-8 Authentication, Role-Based Access Control, and Tenant Identity  
> **Branch:** `feat/authentication-and-roles`  
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

---

*Document revised for PR #23 (FR-8: feat/authentication-and-roles).*
