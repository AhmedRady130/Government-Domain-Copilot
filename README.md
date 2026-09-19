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
- `data` — committed synthetic corpus, manifest, and golden evaluation dataset assets.

## Features

### Chat-completion providers

Gemini is the default chat-completion provider. A separately managed local Ollama
server is also supported through the provider-neutral chat abstraction. Set
`LLM_PRIMARY_PROVIDER=Ollama`, set `LLM_PRIMARY_MODEL` to a pulled Ollama chat
model, and set `LLM_OLLAMA_BASE_URL` to the server URL. Ollama uses no API key;
Gemini reads its API key only from the environment. Compose does not run Ollama.

### Document Ingestion Vertical Slice (MVP)

- `POST /api/documents`: Ingests raw document text, normalises line endings/Unicode, deterministically chunks text, and persists `Document` and `DocumentChunk` entities atomically.
- This is JSON text ingestion, not binary-file upload; request-body and source-text limits are enforced before chunking.
- **Multi-Tenancy Guard**: Server-side tenant identity is resolved strictly via `ITenantContext` (headers/config in development, authenticated identity claims in production). Client-supplied request payloads cannot override tenant identity.
- **Safe failure logging**: `src/Application/Observability/SafeExceptionLoggingExtensions.cs` logs bounded diagnostic metadata without exception text.

### Evaluation Harness (FR-3)

- **Golden dataset**: 26 evaluation cases (16 answerable, 4 out-of-corpus refusals, 6 adversarial).
- **3 deterministic metrics**: Retrieval Hit Rate, Groundedness Score, Refusal Correctness.
- **CLI runner**: `dotnet run --project src/EvaluationRunner` with `--dataset` and `--output` options.
- **Per-case tenant isolation** via `AsyncLocal`-based `IEvaluationTenantContext`.
- See [`docs/EVALUATION.md`](docs/EVALUATION.md) for full details.

## 15-Minute Quick Start

Prerequisites: Docker Desktop (or Docker Engine with Compose), .NET SDK 9, and
PowerShell. Node.js is needed only for `src/Web`, which is outside the assessment
Compose stack. A working provider is required to seed or answer: configure Gemini
with a local API key, or configure a separately running Ollama server and models.
Compose does **not** start Ollama.

```powershell
# Fresh clone: create local-only configuration. Set POSTGRES_PASSWORD and, for
# the default path, GEMINI_API_KEY in .env. Never commit .env.
Copy-Item .env.example .env
docker compose up --build -d

# API liveness and PostgreSQL readiness.
$base = 'http://localhost:8080' # Change if API_PORT differs.
Invoke-RestMethod "$base/health"
Invoke-RestMethod "$base/health/live"
Invoke-RestMethod "$base/ready"

# Validate then idempotently seed the manifest-assigned corpus for both tenants.
dotnet run --project src/ClientCli -- validate-corpus
docker compose run --rm cli seed-corpus --api-key gov-key-tenant-a-officer
docker compose run --rm cli seed-corpus --api-key gov-key-tenant-b-officer
```

The `migrate` service applies the existing EF Core migration history before the
API starts and, in Development, creates the synthetic Tenant A/B identities. It
does not reset or drop the named PostgreSQL volume. To retry initialization, use
`docker compose run --rm migrate`.

Make a grounded Tenant A request, then inspect the exact citations returned by
the API. It returns a refusal rather than inventing a fact when evidence is
insufficient.

```powershell
$officerKey = 'gov-key-tenant-a-officer'
$headers = @{ 'X-API-Key' = $officerKey }
$answer = Invoke-RestMethod "$base/api/answer" -Method Post -Headers $headers `
  -ContentType 'application/json' `
  -Body (@{ query = 'What is the synthetic fee and review target for Business Registration and Renewal?' } | ConvertTo-Json)
$answer
$answer.citations | Format-Table sourceReference, title, sequence
```

Run the bounded orchestration/approval path. A grounded draft may return
`pendingApproval`; only a supervisor in the same tenant may decide and execute
it. Execution records a staged training outcome; it does not call a real
government system.

```powershell
$run = Invoke-RestMethod "$base/api/orchestrate" -Method Post -Headers $headers `
  -ContentType 'application/json' `
  -Body (@{ query = 'What is the synthetic fee and review target for Business Registration and Renewal?'; correlationId = 'quickstart-a-001' } | ConvertTo-Json)
$run.pendingApproval

$supervisorHeaders = @{ 'X-API-Key' = 'gov-key-tenant-a-supervisor' }
Invoke-RestMethod "$base/api/approvals/$($run.pendingApproval.requestId)/decide" -Method Post `
  -Headers $supervisorHeaders -ContentType 'application/json' `
  -Body (@{ decision = 'Approved'; comments = 'Synthetic demo approval' } | ConvertTo-Json)
Invoke-RestMethod "$base/api/approvals/$($run.pendingApproval.requestId)/execute" -Method Post `
  -Headers $supervisorHeaders

# Runs expose final citations, agent executions, and approval state. LLM traces
# are queried by run or correlation ID. Sessions are inspectable when supplied.
Invoke-RestMethod "$base/api/runs/$($run.runId)" -Headers $headers
Invoke-RestMethod "$base/api/traces/llm?runId=$($run.runId)" -Headers $headers
Invoke-RestMethod "$base/api/sessions" -Headers $headers
```

For a supplied `sessionId`, inspect `/api/sessions/{sessionId}/messages` with the
same tenant identity. All runs, traces, sessions, and citations are tenant-scoped.

## Environment Variable Matrix

Put local values in `.env`; Compose maps them into application configuration.
Examples are placeholders, never real secrets.

| Variable | Required | Default | Provider/path | Secret | Safe example |
|---|---|---|---|---|---|
| `POSTGRES_PASSWORD` | Yes for Compose | none | PostgreSQL/API/migrate/CLI | Yes | `local-only-password` |
| `POSTGRES_DB` | Optional | `government_domain_copilot` | PostgreSQL database | No | `government_domain_copilot` |
| `POSTGRES_USER` | Optional | `government_domain_copilot` | PostgreSQL user | No | `government_domain_copilot` |
| `API_PORT` | Optional | `8080` | API host port | No | `8080` |
| `ASPNETCORE_ENVIRONMENT` | Optional | `Development` | API/migrate/CLI | No | `Development` |
| `GEMINI_API_KEY` | Required when Gemini is selected | empty | Gemini chat and embeddings | Yes | `replace-with-local-key` |
| `LLM_PRIMARY_PROVIDER` | Optional | `Gemini` | Chat; `Gemini` or `Ollama` | No | `Ollama` |
| `LLM_PRIMARY_MODEL` | Optional | `gemini-2.5-flash` | Selected chat provider | No | `llama3.2` |
| `LLM_OLLAMA_BASE_URL` | Optional | `http://host.docker.internal:11434` | Ollama chat from Compose | No | `http://host.docker.internal:11434` |
| `EMBEDDING_PRIMARY_PROVIDER` | Optional | `Gemini` | Primary embeddings | No | `Ollama` |
| `EMBEDDING_PRIMARY_MODEL` | Optional | `gemini-embedding-2` | Primary embedding model | No | `nomic-embed-text` |
| `EMBEDDING_FALLBACK_PROVIDER` | Optional | `Ollama` | Embedding fallback | No | `Ollama` |
| `EMBEDDING_FALLBACK_MODEL` | Optional | `nomic-embed-text` | Embedding fallback model | No | `nomic-embed-text` |
| `OLLAMA_BASE_URL` | Optional | `http://host.docker.internal:11434` | Ollama embeddings from Compose | No | `http://host.docker.internal:11434` |

The database expects 768-dimensional embeddings. Keep embedding configuration
stable between seeding and querying, and select an Ollama embedding model that
returns 768 dimensions.

## Provider Paths

- **Gemini default chat:** `LLM_PRIMARY_PROVIDER=Gemini` and
  `LLM_PRIMARY_MODEL=gemini-2.5-flash`; default embeddings are Gemini
  `gemini-embedding-2`. Successful Gemini calls need `GEMINI_API_KEY`.
- **Local Ollama chat:** set `LLM_PRIMARY_PROVIDER=Ollama`, select a locally
  pulled `LLM_PRIMARY_MODEL`, and point `LLM_OLLAMA_BASE_URL` at the server.
- **Local Ollama embeddings:** set `EMBEDDING_PRIMARY_PROVIDER=Ollama`, a
  compatible `EMBEDDING_PRIMARY_MODEL`, and `OLLAMA_BASE_URL`; the adapter uses
  Ollama's `/api/embed` path.
- **Ollama prerequisite:** run the server yourself and pull configured models,
  for example `ollama pull llama3.2` and `ollama pull nomic-embed-text`. Compose
  does **not** run, pull, or manage Ollama.

Only `Gemini` and `Ollama` are registered chat providers; an invalid selection
fails safely. Provider choice and URLs are server-side configuration and cannot
be overridden by API requests.

## Tests and Evaluation

Run from the repository root:

```powershell
dotnet build .\GovernmentDomainCopilot.sln --configuration Release
dotnet test .\GovernmentDomainCopilot.sln --configuration Release
dotnet run --project src/ClientCli -- validate-corpus
dotnet run --project src/EvaluationRunner
dotnet run --project src/EvaluationRunner -- --output "$env:TEMP\government-domain-evaluation.json"
```

The corpus validator is a filesystem/manifest check and fails for invalid
metadata, safety markers, pages, or tenant distribution. The evaluation harness
uses its embedded golden dataset and in-memory database; it exits `0` when all
cases pass, `1` for failed cases, `2` for dataset-load errors, and `3` for
harness errors. Integration tests require Docker/Testcontainers.

## Seeded Development Accounts

These deterministic Development-only identities are synthetic, non-production
credentials; production rejects them.

| Tenant | Identity | Role | Synthetic API key |
|---|---|---|---|
| Tenant A | `officer-a` | Officer | `gov-key-tenant-a-officer` |
| Tenant A | `supervisor-a` | Supervisor | `gov-key-tenant-a-supervisor` |
| Tenant B | `officer-b` | Officer | `gov-key-tenant-b-officer` |
| Tenant B | `supervisor-b` | Supervisor | `gov-key-tenant-b-supervisor` |

Use `X-API-Key` (or `Authorization: ApiKey <key>`). The server derives tenant
identity from the authenticated identity; client-supplied tenant IDs are ignored.

## 5-Minute Demo Path

With the stack started, a provider configured, and both tenants seeded:

1. Authenticate with the Tenant A officer key and call `POST /api/answer` using
   the Business Registration query in the quick start.
2. Show `citations[0].sourceReference`, `title`, and `sequence`: the answer is
   grounded in the seeded synthetic corpus, never real government guidance.
3. Stream the same bounded workflow. It emits progress, provider-neutral answer
   chunks when generated, and an approval event when a draft is staged.

   ```powershell
   curl.exe --no-buffer -X POST "$base/api/orchestrate/stream" `
     -H "X-API-Key: $officerKey" -H 'Content-Type: application/json' `
     -d '{"query":"What is the synthetic fee and review target for Business Registration and Renewal?","correlationId":"demo-stream-001"}'
   ```

4. Approve and execute the returned request with the Tenant A supervisor key,
   then show `/api/runs/{runId}`, `/api/traces/llm?runId={runId}`, and, when a
   session was used, `/api/sessions/{sessionId}/messages`.

## Troubleshooting

- **PostgreSQL unavailable or readiness `503`:** inspect `docker compose logs postgres`,
  confirm `POSTGRES_PASSWORD`, then retry `docker compose up --build -d`.
- **Migrations:** inspect `docker compose logs migrate`, correct connection
  settings, and run `docker compose run --rm migrate`; do not delete the volume.
- **API port conflict:** change `API_PORT` in `.env`, restart Compose, and update `$base`.
- **Gemini key missing:** health/migrations work, but default Gemini seed/chat calls fail
  until `GEMINI_API_KEY` is set.
- **Ollama unavailable:** start the separately managed server and verify the URL is
  reachable from the API container (`host.docker.internal:11434` is the Docker Desktop default).
- **Ollama model not pulled:** run `ollama pull` for chat and embedding models; the
  embedding model must return 768 dimensions.
- **Invalid provider selection:** use exactly `Gemini` or `Ollama` in
  `LLM_PRIMARY_PROVIDER`, then restart Compose.
- **Corpus seed authentication:** use a Development-only synthetic `--api-key`, wait
  for migrations, and do not attempt a tenant override.
- **Docker/Testcontainers unavailable:** start Docker Desktop before `dotnet test`;
  container-backed integration tests require a working Docker daemon.

## Documentation

- [Business requirements](docs/BRD.md)
- [System design](docs/SYSTEM-DESIGN.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Security controls](docs/SECURITY.md)
- [Synthetic corpus](docs/CORPUS.md)
- [Evaluation harness](docs/EVALUATION.md)
- [Orchestration and approval](docs/orchestration.md)
- [Observability](docs/OBSERVABILITY.md)
- [Architecture decision records](docs/adr/)
