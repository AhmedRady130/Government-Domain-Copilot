# Contributing to Government Domain Copilot

Thank you for improving Government Domain Copilot. This public assessment
repository is a government-domain retrieval-augmented-generation MVP. Keep
changes focused, reviewable, safe for multi-tenant use, and supported by tests.

## Prerequisites

- .NET SDK 9
- Docker Desktop or Docker Engine with Compose for PostgreSQL/pgvector
- Node.js and npm only when changing `src/Web`
- A local Ollama server and installed model only when exercising Ollama

## Local setup and checks

1. Fork and clone the repository, then create a descriptive feature branch.
2. Copy the template: `Copy-Item .env.example .env`.
3. Set a unique local-only `POSTGRES_PASSWORD` in `.env`.
4. Restore, build, and test:

   ```powershell
   dotnet restore .\GovernmentDomainCopilot.sln
   dotnet build .\GovernmentDomainCopilot.sln --configuration Release --no-restore
   dotnet test .\GovernmentDomainCopilot.sln --configuration Release --no-restore
   git diff --check
   ```

Do not commit `.env`, API keys, tokens, passwords, or real personal data.

## Docker and database workflow

Start the normal stack with `docker compose up --build`. It runs PostgreSQL, a
one-shot migration service, and the API. PostgreSQL stays internal to the
Compose network and is not published to the host. Rerun idempotent migrations
with `docker compose run --rm migrate`; do not reset volumes to recover from a
migration problem.

Schema work belongs in `src/Infrastructure` and needs an EF Core migration plus
relevant persistence tests. Preserve migration history: do not rewrite, delete,
or casually alter existing migrations. Test PostgreSQL/pgvector behavior when
Docker/Testcontainers is available.

The `cli` service is opt-in and does not run during normal `docker compose up`.
After the stack is ready, use it for the authenticated corpus seed workflow:

```powershell
dotnet run --project src/ClientCli -- validate-corpus
docker compose run --rm cli seed-corpus --api-key gov-key-tenant-a-officer
docker compose run --rm cli seed-corpus --api-key gov-key-tenant-b-officer
```

`validate-corpus` is database-independent. `seed-corpus` mutates persistence,
requires an authenticated development API key, and rejects tenant overrides.
It is idempotent by tenant and source reference. Read
[docs/CORPUS.md](docs/CORPUS.md) and [docs/SECURITY.md](docs/SECURITY.md) before
changing corpus or identity behavior.

## Architecture and safety

- `Domain` is independent of frameworks, providers, databases, and EF Core.
- `Application` depends only on `Domain` and contains use cases/abstractions.
- `Infrastructure` implements persistence and external-provider adapters.
- `API` is the outer host. Clients must not control tenant identity, provider
  URLs, or other server-side security decisions.

All tenant-scoped reads, writes, retrieval, sessions, approvals, and traces use
the authenticated server-side tenant context. Never add a client tenant override
or permit cross-tenant access. Grounded answers require tenant-scoped retrieved
evidence and verified citations; insufficient evidence must refuse rather than
guess. Treat ingested and retrieved text as untrusted data, never instructions.

Never log or commit secrets, authorization headers, prompts, raw evidence, or
arbitrary provider response bodies. Keep consequential actions behind the
existing supervisor approval boundary. Follow [docs/SECURITY.md](docs/SECURITY.md)
and the repository's `AGENTS.md`; the latter is agent guidance, not a substitute
for human review.

## Provider configuration

Gemini is the default chat-completion provider and reads `GEMINI_API_KEY` only
from environment configuration. The local Ollama path uses
`LLM_PRIMARY_PROVIDER=Ollama`, an installed `LLM_PRIMARY_MODEL`, and a
server-side-only `LLM_OLLAMA_BASE_URL`; Ollama needs no API key. Compose does not
start Ollama, so its server/model must already be reachable from the API
container. Do not expose provider URLs as client parameters.

Embedding settings are separate from chat settings. Check `.env.example` and
ensure the configured embedding provider/model is reachable before ingestion.

## Tests, documentation, and pull requests

Add meaningful tests with each behavior change: unit tests for Domain/Application
logic, contract tests for endpoint/tool contracts, and integration tests for
persistence, ingestion, retrieval, and provider adapters. Prefer deterministic
HTTP fakes for provider behavior. Report unavailable Docker/Testcontainers rather
than weakening tests.

Update documentation whenever commands, configuration, security boundaries, or
observable behavior changes. Preserve and cross-reference the existing corpus,
evaluation, orchestration, observability, and security docs. Record significant
architecture decisions in `docs/adr/`.

Use focused Conventional Commit messages and scoped pull requests. Describe the
motivation, behavior/security impact, tests run and results, and limitations.
Before requesting review, run the checks above plus focused workflow tests for
the change.

## Troubleshooting

- **PostgreSQL unavailable:** verify `POSTGRES_PASSWORD`, then inspect
  `docker compose logs postgres`.
- **Migration failure:** inspect `docker compose logs migrate`, correct local
  configuration, and rerun the migration service.
- **Port conflict:** change `API_PORT` in `.env`.
- **Gemini failure:** provide `GEMINI_API_KEY` only through local environment
  configuration; health and migrations do not require it.
- **Ollama failure:** start the configured server, pull the model, and confirm
  the configured URL is reachable from the API container.
- **Corpus seed failure:** validate first, use an authenticated development key,
  and ensure migrations completed.
- **Integration containers unavailable:** make Docker/Testcontainers available,
  then rerun the affected integration tests.
