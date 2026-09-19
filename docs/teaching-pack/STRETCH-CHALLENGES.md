# Stretch Challenges

## 1. Policy-driven PII handling

Replace regex-only handling with configurable detectors, confidence scores, and explicit actions: redact, reject, or quarantine.

**Hints**

- Keep contracts in Application.
- Never retain raw matches in logs or audit records.
- Add false-positive and false-negative regression tests.

## 2. PostgreSQL RLS defense in depth

Implement transaction-scoped tenant context and Row-Level Security policies for all tenant-owned tables.

**Hints**

- Keep Application tenant predicates; do not replace them.
- Ensure pooled connections cannot retain the prior tenant setting.
- Test direct SQL under Tenant A cannot read Tenant B documents, vectors, sessions, or traces.

## 3. Adversarial evaluation quality gate

Make CI fail when refusal correctness regresses for prompt injection, cross-tenant probing, or fabricated legal obligations.

**Hints**

- Use deterministic fake providers for unit tests.
- Report safe aggregate metrics only.
- Keep structural citation validation distinct from semantic legal review.

## 4. Workflow supply-chain policy

Create a CI check that rejects mutable third-party action references unless a documented exception exists.

**Hints**

- Store approved SHA-to-release mappings in reviewable configuration.
- Let Dependabot propose updates.
- Run the policy before deployment jobs.
