# Government Domain Copilot

Foundation for an agentic retrieval-augmented-generation platform in a government domain.

## Structure

- `src/Domain` — core domain layer; framework and infrastructure independent.
- `src/Application` — application use-case and abstraction layer; depends only on Domain.
- `src/Infrastructure` — external-service, EF Core PostgreSQL persistence, and chunking implementations.
- `src/API` — ASP.NET Core host exposing minimal API endpoints (`POST /api/documents`).
- `src/EvaluationRunner` — CLI tool for running the evaluation harness against golden datasets.
- `src/Web` — Angular web application.
- `tests` — unit (`Domain.Tests`, `Application.Tests`), contract (`Contract.Tests`), and integration (`Integration.Tests`) test projects.
- `docs/adr` — architecture decision records.
- `data` — golden evaluation datasets.

## Features

### Document Ingestion Vertical Slice (MVP)

- `POST /api/documents`: Ingests raw document text, normalises line endings/Unicode, deterministically chunks text, and persists `Document` and `DocumentChunk` entities atomically.
- **Multi-Tenancy Guard**: Server-side tenant identity is resolved strictly via `ITenantContext` (headers/config in development, authenticated identity claims in production). Client-supplied request payloads cannot override tenant identity.

### Evaluation Harness (FR-3)

- **Golden dataset**: 26 evaluation cases (16 answerable, 4 out-of-corpus refusals, 6 adversarial).
- **3 deterministic metrics**: Retrieval Hit Rate, Groundedness Score, Refusal Correctness.
- **CLI runner**: `dotnet run --project src/EvaluationRunner` with `--dataset` and `--output` options.
- **Per-case tenant isolation** via `AsyncLocal`-based `IEvaluationTenantContext`.
- See [`docs/evaluation.md`](docs/evaluation.md) for full details.

## Prerequisites

- Docker Desktop (or Docker Engine with Compose)
- .NET SDK 9 for non-container development and tests
- Node.js and npm only when working on `src/Web`

## Docker quick start

The minimal runnable stack is PostgreSQL 16 with pgvector and the API. The web
project is intentionally not part of this assessment stack.

1. Copy the local configuration template: `Copy-Item .env.example .env`.
2. Set a unique local `POSTGRES_PASSWORD` in `.env`. Add `GEMINI_API_KEY` only
   when exercising embedding or answer endpoints; it is not needed for startup
   checks.
3. Start the reproducible stack: `docker compose up --build`.
4. In another terminal, verify the API: `Invoke-WebRequest http://localhost:8080/health`,
   `Invoke-WebRequest http://localhost:8080/health/live`, and
   `Invoke-WebRequest http://localhost:8080/ready`.

Compose waits for PostgreSQL's `pg_isready` health check, then runs the one-shot
`migrate` service. It applies the existing EF Core migration history and, only
in `Development`, creates the existing synthetic Tenant A/B identities. The API
starts only after that service succeeds. This operation is idempotent and never
resets or drops the named `postgres-data` volume. To rerun initialization against
an already-running database, use `docker compose run --rm migrate`.

For a development-only authenticated request, the existing synthetic key may be
used explicitly (it is disabled outside Development and is not a production
credential):

```powershell
Invoke-WebRequest http://localhost:8080/api/sessions `
  -Headers @{ "X-API-Key" = "gov-key-tenant-a-officer" }
```

The initialization step deliberately does not seed a government corpus; that is
reserved for the follow-up corpus-seeding work.

## Synthetic D4 government corpus

The committed synthetic corpus is in `data/corpus/`. It contains 32 documents
and 160 explicit plain-text pages, with no real personal data. Validation is a
pure filesystem/manifest check and does not require PostgreSQL. Seed each
existing synthetic tenant with its authenticated development API key:

```powershell
dotnet run --project src/ClientCli -- validate-corpus
docker compose run --rm cli seed-corpus --api-key gov-key-tenant-a-officer
docker compose run --rm cli seed-corpus --api-key gov-key-tenant-b-officer
```

See [`docs/CORPUS.md`](docs/CORPUS.md) for the assessment mapping, metadata,
page validation, safety, and idempotency details.

### Troubleshooting

- **PostgreSQL not ready:** inspect `docker compose logs postgres`; check that
  `POSTGRES_PASSWORD` is set and retry `docker compose up --build`.
- **Port conflict:** change `API_PORT` in `.env`, then use that port in checks.
- **Missing Gemini key:** health and migrations still work; set `GEMINI_API_KEY`
  before calling endpoints that need Gemini.
- **Migration failure:** inspect `docker compose logs migrate`; correct the
  connection settings and rerun `docker compose run --rm migrate`. Do not delete
  the volume as a recovery shortcut.
- **Rebuild after code changes:** run `docker compose up --build`; use
  `docker compose down` to stop services while preserving the database volume.

## Verification

```powershell
dotnet build .\GovernmentDomainCopilot.sln --configuration Release
dotnet test .\GovernmentDomainCopilot.sln --configuration Release
```
