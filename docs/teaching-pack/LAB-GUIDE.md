# 90-Minute Lab: Secure the Delivery Pipeline

## Learning outcomes

Learners will be able to:

1. Explain lockfile-based reproducible installation.
2. Inspect secret and dependency scanning in CI.
3. Verify formatting, tests, and immutable action references.
4. Trace Application-layer PII redaction before ingestion persistence.
5. Explain tenant-scoped vector retrieval and grounded-answer refusal.

## Prerequisites

- .NET 9 SDK, Node.js, and Docker Desktop for pgvector integration tests.
- A fork with GitHub Actions enabled.
- Synthetic data only: never use a real credential or real personal data.

## Schedule

| Time | Activity |
|---:|---|
| 0–10 min | Architecture and threat-model briefing |
| 10–30 min | Lockfile and dependency-security review |
| 30–50 min | Workflow hardening review |
| 50–70 min | PII redaction practical |
| 70–82 min | Tenant and prompt-safety tests |
| 82–90 min | Review and assessment |

## Task 1 — Lockfile and dependency audit

1. Inspect \`src/Web/package.json\` and \`src/Web/package-lock.json\`.
2. Run:

\`\`\`powershell
cd src/Web
npm ci
npm audit --audit-level=low
npm run build
\`\`\`

3. Explain why \`npm ci\` is preferred in CI.

### Expected output

- Installation is derived from the committed lockfile.
- Angular builds successfully.
- The audit produces an actionable vulnerability result.

## Task 2 — Security workflow review

1. Inspect \`.github/workflows/security.yml\`.
2. Identify Gitleaks, NuGet audit, npm audit, and Dependency Review.
3. Confirm the secret scan checks full history and receives \`GITHUB_TOKEN\`.
4. Review the action-reference pinning policy.

### Expected output

A table listing each scan, trigger, and failure behavior.

## Task 3 — CI quality gates

\`\`\`powershell
dotnet format GovernmentDomainCopilot.sln --verify-no-changes --no-restore
dotnet build GovernmentDomainCopilot.sln --configuration Release --no-restore
dotnet test GovernmentDomainCopilot.sln --configuration Release --no-restore
git diff --check
\`\`\`

### Expected output

- No formatting or whitespace errors.
- Passing unit, contract, and integration tests when Docker is available.
- A clear distinction between an application test failure and unavailable Testcontainers infrastructure.

## Task 4 — PII redaction practical

1. Read \`src/Application/Documents/Security/RegexPiiRedactor.cs\`.
2. Locate its dependency-injection registration.
3. Trace its use in \`DocumentEndpoints\` before \`IngestDocumentUseCase\`.
4. Add synthetic email, phone, and national-ID input to a test.

### Expected output

- Persisted chunk content contains redaction markers.
- Original values do not appear in persisted content or logs.
- Audit logs contain counts only.

## Task 5 — Tenant and prompt boundaries

1. Inspect the tenant predicates in \`PgVectorChunkRetriever\` and \`PgKeywordChunkRetriever\`.
2. Run tenant-isolation integration tests.
3. Inspect direct, indirect, and secret-exfiltration attacks in the golden evaluation dataset.

### Expected output

- Tenant B cannot retrieve or cite Tenant A evidence.
- At least three instruction-attack cases require refusal.
- Learners explain why RLS is defense in depth, not a replacement for correct application queries.

## Assessment mapping

| Criterion | Evidence |
|---|---|
| Secure supply chain | lockfile, audit commands, workflow review |
| OWASP application security | headers, CORS, limits, safe logging |
| LLM safety | untrusted evidence and refusal cases |
| T0 tenancy | retrieval predicates and leakage tests |
| Engineering quality | tests, formatting, CI checks |

## Common mistakes

- Using \`npm install\` in CI and rewriting the lockfile.
- Adding fake values that resemble real secrets.
- Allowing wildcard CORS with credentials.
- Logging matched PII to prove the detector worked.
- Tenant-filtering documents but not vector/keyword retrieval.
- Treating unavailable Docker as a successful integration test.
