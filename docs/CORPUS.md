# Synthetic Government Corpus

## Assessment requirement

The original ITI Technical Instructor Assessment requires: “Corpus: ≥30 documents / 150+ pages, public sources or synthetic data you generate. Never use real personal data — this invalidates the submission outright.” This repository uses only synthetic data. D4 is **Government — citizen services & regulations**, covering the workflow: citizen situation, service and eligibility, documents, fees, timelines, and an official response draft.

## Contents and safety

`data/corpus/` contains 32 committed `.txt` guides: 16 assigned to existing synthetic Tenant A (`11111111-1111-1111-1111-111111111111`) and 16 to Tenant B (`22222222-2222-2222-2222-222222222222`). Each guide contains five explicit plain-text form-feed pages, for **160 pages** total. A form feed is the native page-break control character for paginated plain text; this is an actual explicit pagination, not a character-count page-equivalent. The validator additionally rejects pages with fewer than 800 non-whitespace characters.

Every guide contains the disclaimer “Synthetic training/demo data — not an official government publication.” Topics cover civil records, planning, public health, commerce, transport, revenue, safety, recreation, and social/community services. The material uses only obviously synthetic identifiers, zones, fees, timelines, names, and references. It contains no real people, addresses, emails, telephone numbers, citizen IDs, secrets, or credentials.

`manifest.json` provides stable ID, title, tenant assignment, category, source reference, file, declared page count, and synthetic designation. The existing `Document` model stores the supported ingestion metadata (title and source reference); tenant ownership continues to come solely from authenticated server-side context.

## Validate

From the repository root, this is a pure filesystem/manifest check and does not
initialize PostgreSQL or EF Core:

```powershell
dotnet run --project src/ClientCli -- validate-corpus
```

The command fails for missing metadata, duplicate IDs or references, non-synthetic entries, missing disclaimers, page mismatches, insufficient document/page counts, or insubstantial pages.

## Seed reproducibly

The seed command reads the committed manifest and uses the existing `IIngestDocumentUseCase`. It requires an authenticated synthetic development API key and accepts no tenant override. It selects only documents assigned to the tenant resolved from that authenticated identity:

```powershell
dotnet run --project src/ClientCli -- seed-corpus --api-key gov-key-tenant-a-officer
dotnet run --project src/ClientCli -- seed-corpus --api-key gov-key-tenant-b-officer
```

Run the same command again to re-ingest without duplicate logical documents: the existing repository treats `(TenantId, SourceReference)` as the idempotency key and replaces that document’s chunks atomically. This preserves tenant isolation and manifest-controlled assignment.

For Docker, start the existing stack with `docker compose up -d`, then use the opt-in `cli` service. It receives the same standard `ConnectionStrings__GovernmentDomainCopilot` configuration as the API, resolves `postgres` on the internal Compose network, and reads its database password only from local Compose environment configuration.

```powershell
docker compose run --rm cli seed-corpus --api-key gov-key-tenant-a-officer
docker compose run --rm cli seed-corpus --api-key gov-key-tenant-b-officer
```

The `cli` service has a Compose profile, so normal `docker compose up` behavior is unchanged; explicitly targeting it with `docker compose run` runs it on demand. It exposes no database port and adds no tenant override. Outside Compose, commands that need persistence use the CLI's safe in-memory fallback only when no connection string is configured; `seed-corpus` still requires an authenticated API key.

`tools/GenerateSyntheticCorpus.ps1` is the deterministic source generator used to produce the committed text assets and manifest; it contains no external fetch or real data source.
