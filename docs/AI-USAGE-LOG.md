# AI Usage Log & Human Oversight Audit

## Overview
This document logs the interaction history, technical challenges encountered across full-stack development, AI proposals, human oversight decisions, and resolution paths during the build of the **Government-Domain-Copilot** system.

---

## Incident Log 1: Angular Lockfile Sync & Missing Native Dependencies (`npm ci`)
- **Category:** Frontend & CI/CD
- **Context:** GitHub Actions `npm ci` failed on the Angular web project during frontend build stages.
- **Root Cause:** Lockfile out-of-sync and missing platform-specific native binaries (`@emnapi/core`, `@emnapi/runtime`).
- **AI Proposal:** Suggested running basic `npm install --legacy-peer-deps` directly on the root without cleaning cache.
- **Human Correction & Action:** Rejected the root suggestion; isolated the issue to `src/Web`, cleared `node_modules` and `package-lock.json` locally, performed a clean `npm install`, verified the updated lockfile diff, and pushed the correct lockfile.

---

## Incident Log 2: Gitleaks Token Authentication Breakage in Pull Requests
- **Category:** Security Governance
- **Context:** `gitleaks-action` failed across PR scans despite passing on local runs.
- **Root Cause:** Breaking change in Gitleaks requiring explicit `GITHUB_TOKEN` environment binding for PR events.
- **AI Proposal:** Provided a updated `.github/workflows/security.yml` step injecting `env: GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}`.
- **Human Action & Verification:** Applied YAML structure, validated syntax via `git diff`, and verified all PR security checks turned green.

---

## Incident Log 3: EF Core Migration & Database Connection Resilience
- **Category:** Backend (.NET / Entity Framework Core)
- **Context:** Application failed to start in Docker containers during initial database migration bootstrap.
- **Root Cause:** Race condition where the .NET Web API started before PostgreSQL container became fully ready to accept connections.
- **AI Proposal:** Recommended adding static `Thread.Sleep()` calls in `Program.cs` before running EF Core migrations.
- **Human Correction & Action:** Rejected static sleep as brittle; implemented an exponential backoff retry strategy with `Polly` policy during application startup and added container healthchecks in `docker-compose.yml`.

---

## Incident Log 4: Integration & Contract Test Environment Isolation
- **Category:** Testing & Quality Assurance
- **Context:** Contract and WebApplicationFactory integration tests intermittently failed due to shared database state across parallel test runners.
- **Root Cause:** Missing isolation in `ContractWebApplicationFactory.cs` causing state contamination between test runs.
- **AI Proposal:** Suggested running tests sequentially with `[CollectionDefinition(DisableParallelization = true)]`.
- **Human Correction & Action:** Optimized test performance by configuring `Respawn` or SQLite In-Memory database checkpoints per test fixture rather than disabling parallel test execution entirely.

---

## Incident Log 5: Docker Container Optimization & Multi-Stage Builds
- **Category:** DevOps & Infrastructure
- **Context:** Initial Docker image size was exceeding 1.2 GB and build times were slow in CI.
- **Root Cause:** Including full SDKs and non-essential Angular build caches inside final runtime images.
- **AI Proposal:** Generated multi-stage `Dockerfile` definitions isolating `.NET SDK` / `Node.js` build stages from lightweight ASP.NET runtime and Nginx base images.
- **Human Action & Verification:** Reviewed docker layers, added `.dockerignore` rules to prevent pushing `bin/obj/node_modules`, reducing final image size down to <200 MB.

---

## Summary Matrix
| Area | Issue Description | AI Role | Human Intervention | Status |
| :--- | :--- | :--- | :--- | :--- |
| **Frontend** | Angular Lockfile & `@emnapi` sync | Generic `npm` fix | Isolated to `src/Web` & regenerated clean lockfile | Resolved |
| **Security** | Gitleaks PR authentication | Provided YAML snippet | Injected `GITHUB_TOKEN` & verified PR action | Resolved |
| **Backend** | Database migration startup race | Suggested `Thread.Sleep` | Replaced with resilience policy & healthchecks | Resolved |
| **Testing** | Contract Test state pollution | Disabling parallel tests | Implemented test state reset (Respawn/SQLite) | Resolved |
| **DevOps** | Heavy Docker runtime image size | Drafted multi-stage build | Added `.dockerignore` & tuned runtime layers | Resolved |
