# ADR 0001: PostgreSQL and EF Core database foundation

## Status

Accepted

## Context

The platform needs a minimal, provider-backed persistence boundary without introducing government-domain entities or business functionality.

## Decision

Use Entity Framework Core with the Npgsql PostgreSQL provider. The DbContext, provider configuration, migrations, and EF Core tooling support live in Infrastructure. API supplies configuration to Infrastructure's service-registration extension. Connection strings use the standard `ConnectionStrings__GovernmentDomainCopilot` environment-variable convention and are not committed.

PostgreSQL is a mature relational database with strong .NET ecosystem support. Its future pgvector extension can allow the same PostgreSQL instance to support vector retrieval when that capability is explicitly introduced. pgvector is not installed or configured by this decision.

## Consequences

- Domain and Application remain free of EF Core, Npgsql, PostgreSQL, and ASP.NET Core persistence dependencies.
- The initial migration intentionally contains no schema objects because no domain entities have been defined.
- EF Core migrations can create or update a configured development database once a real local connection string is supplied.
