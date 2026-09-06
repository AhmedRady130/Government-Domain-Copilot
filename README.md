# Government Domain Copilot

Foundation for an agentic retrieval-augmented-generation platform in a government domain.

## Structure

- `src/Domain` — core domain layer; framework and infrastructure independent.
- `src/Application` — application use-case and abstraction layer; depends only on Domain.
- `src/Infrastructure` — external-service, EF Core PostgreSQL persistence, and chunking implementations.
- `src/API` — ASP.NET Core host exposing minimal API endpoints (`POST /api/documents`).
- `src/Web` — Angular web application.
- `tests` — unit (`Domain.Tests`, `Application.Tests`), contract (`Contract.Tests`), and integration (`Integration.Tests`) test projects.
- `docs/adr` — architecture decision records.

## Features

### Document Ingestion Vertical Slice (MVP)

- `POST /api/documents`: Ingests raw document text, normalises line endings/Unicode, deterministically chunks text, and persists `Document` and `DocumentChunk` entities atomically.
- **Multi-Tenancy Guard**: Server-side tenant identity is resolved strictly via `ITenantContext` (headers/config in development, authenticated identity claims in production). Client-supplied request payloads cannot override tenant identity.

## Prerequisites

- .NET SDK 9
- Node.js and npm

## Verification

```powershell
dotnet build .\GovernmentDomainCopilot.sln --configuration Release
dotnet test .\GovernmentDomainCopilot.sln --configuration Release
```
