# Government Domain Copilot — Teaching Pack

## Slide 1 — Goal

Build a safe, evidence-grounded Government Domain Copilot for citizen services and regulations.

- D4: answers are grounded in retrieved evidence.
- T0: every read, write, and retrieval action is tenant-scoped.
- The current solution targets **.NET 9**, Angular, Docker, and GitHub Actions.

---

## Slide 2 — Problem

- Citizens need clear, cited information about procedures, eligibility, documents, fees, and timelines.
- Government facts must not be fabricated.
- Tenants must never access each other's data.

---

## Slide 3 — Clean Architecture

\`\`\`text
API → Application → Domain
          ↑
    Infrastructure
\`\`\`

- Domain contains business rules and no framework/provider dependencies.
- Application owns use cases, abstractions, policies, and orchestration.
- Infrastructure implements EF Core, PostgreSQL/pgvector, auth, and LLM adapters.

---

## Slide 4 — Main Components

- Angular browser client.
- ASP.NET Core API and boundary middleware.
- Application services for ingestion, retrieval, grounded answers, and agents.
- PostgreSQL + pgvector for tenant-scoped data.
- Gemini/Ollama implementations behind Application abstractions.

---

## Slide 5 — Document Ingestion

\`\`\`text
JSON → size/content-type checks → PII redaction
→ validation → chunking → tenant-scoped persistence → embeddings
\`\`\`

- The endpoint accepts JSON text, not binary uploads.
- Emails, phones, and 14-digit national IDs are redacted before persistence.
- Logs retain category counts, not raw PII.

---

## Slide 6 — Grounded Answer Flow

\`\`\`text
Question → tenant context → vector + keyword retrieval
→ fusion → bounded evidence → LLM → citation validation
→ grounded answer or refusal
\`\`\`

---

## Slide 7 — Agentic Workflows

- Specialist agents run in a bounded sequence.
- Tools have typed contracts and explicit allow-lists.
- Approval-required actions cannot execute while pending or rejected.
- Iterations, timeouts, retries, and fallback are bounded.

---

## Slide 8 — Prompt Injection Defense

- System instructions explicitly classify retrieved content as untrusted data.
- Evidence is separated from trusted instructions.
- Injection text cannot override grounding policy.
- Missing evidence or invalid citations cause refusal.

---

## Slide 9 — Legal Safety

- Never invent rights, obligations, fees, deadlines, or procedures.
- Every factual answer requires citations.
- Ambiguous or unsupported questions receive a refusal and should be escalated to an authoritative process.

---

## Slide 10 — Tenant Isolation

- Tenant identity is server-side and authenticated.
- Client-supplied tenant IDs and spoofed headers are ignored.
- Relational, keyword, vector, session, trace, and embedding queries filter by tenant.
- Integration tests prove cross-tenant isolation.

---

## Slide 11 — Database Boundary

- Tenant-aware keys and foreign keys protect relationships.
- pgvector and keyword queries predicate on \`TenantId\`.
- PostgreSQL RLS is planned defense in depth; application predicates remain mandatory.

---

## Slide 12 — HTTP Controls

- Restrictive CSP, \`nosniff\`, frame denial, referrer policy, HTTPS-only HSTS.
- Explicit configured CORS origins; no wildcard credentialed CORS.
- Body/query limits and tenant/IP-partitioned rate limits.

---

## Slide 13 — Safe Observability

- Correlation IDs link requests, tools, runs, and traces.
- Safe failure logs contain code, operation, type, correlation ID, and duration.
- Do not log prompts, evidence, credentials, provider response bodies, or exception text.

---

## Slide 14 — Supply Chain Security

- Gitleaks scans full Git history.
- NuGet audit, npm audit, Dependency Review, and Dependabot run in CI.
- GitHub Actions are reviewed and pinned according to project policy.
- \`.env\` files are ignored; production secrets come from external configuration.

---

## Slide 15 — CI Pipeline

\`\`\`text
restore → build → format verification → unit tests
→ contract tests → integration tests → security scans
\`\`\`

- Angular installs from \`package-lock.json\` with \`npm ci\`.
- Security jobs run on PRs, pushes, schedules, and manual dispatch.

---

## Slide 16 — Common Pitfalls

- Trusting tenant IDs from JSON, query parameters, or headers.
- Vector searching without a tenant predicate.
- Treating retrieved text as trusted instructions.
- Persisting raw PII or logging exceptions with sensitive content.
- Using mutable third-party actions without review.

---

## Slide 17 — Verification

\`\`\`powershell
dotnet test GovernmentDomainCopilot.sln --configuration Release --no-restore
dotnet format GovernmentDomainCopilot.sln --verify-no-changes --no-restore
git diff --check
\`\`\`

---

## Slide 18 — Takeaways

- Security is an architectural property.
- Grounding, tenancy, and approval boundaries make AI useful without making it authoritative.
- Tests and CI turn security intentions into enforceable controls.
