# ADR 0005: Vector Similarity Retrieval for Government Domain Copilot

## Status

Accepted

## Context

Following the implementation of pgvector embedding persistence in PR #14 (ADR 0004), the Government Domain Copilot platform requires vector similarity retrieval capabilities to find relevant government document chunks for user queries. Vector retrieval must maintain strict multi-tenancy boundaries (T0), Clean Architecture, and safe error handling while laying the groundwork for future RAG features.

## Decision

1. **Clean Architecture Retrieval Abstractions**:
   - Introduce `IVectorSearchUseCase` in Application (`src/Application/Retrieval/Abstractions`) as the high-level application search contract.
   - Introduce `IChunkRetriever` in Application (`src/Application/Retrieval/Abstractions`) as the internal data layer retrieval abstraction implemented in Infrastructure (`src/Infrastructure/Retrieval/PgVectorChunkRetriever.cs`).
   - Keep domain and application models entirely free of EF Core and `Pgvector.Vector` types.

2. **Strict Multi-Tenancy (T0)**:
   - Public `VectorSearchRequest` and API query parameters must **NOT** accept caller-supplied `TenantId`.
   - `TenantId` is resolved exclusively inside the application use case via `ITenantContext.GetTenantId()`.
   - Every PostgreSQL vector query filters by `TenantId` at the database query level (`c.TenantId == tenantId && c.Embedding != null`). Global candidate fetching with in-memory tenant filtering is strictly prohibited.

3. **Cosine Distance & Similarity Score Contract**:
   - PostgreSQL pgvector evaluates similarity using cosine distance (`<->` operator mapped via `c.Embedding.CosineDistance(queryVector)`), matching the HNSW index (`vector_cosine_ops`) created in PR #14.
   - Results return raw cosine `Distance` (range `0.0` to `2.0`, where lower distance indicates a closer vector match) and 1-based `Rank`.
   - Raw distance and rank are **never** presented or calculated as probability or confidence scores. Normalized score transformations (`1.0 - distance`) are explicitly omitted for this MVP phase.

4. **Query Embedding & Dimension Validation**:
   - Query text is converted to a vector embedding using the existing `IEmbeddingService`.
   - Exactly one query embedding is generated per request.
   - Vector dimension is validated against the configured expected size (768). Dimension mismatches fail safely.

5. **Limits & Observability Safeguards**:
   - Centralize limits in `VectorSearchLimits` (`DefaultTopK = 5`, `MaxTopK = 50`, `ExpectedDimension = 768`).
   - Reject empty or whitespace query inputs.
   - Operational logs include tenant ID, provider, model, topK, result count, and duration.
   - Full query text, document content, and raw vector floats are **never logged**.

6. **MVP Scope Boundary**:
   - PR #15 implements **vector similarity retrieval only**.
   - Keyword search (BM25 / PostgreSQL `tsvector`), hybrid retrieval fusion, reranking, HyDE, multi-query, and LLM answer generation are explicitly deferred to subsequent PRs to satisfy FR-2 requirements.

## Consequences

- The platform gains robust, tenant-isolated vector similarity search backed by pgvector's HNSW index.
- Clean Architecture principles and multi-tenancy security invariants are strictly enforced.
- Comprehensive unit and PostgreSQL integration test coverage validates retrieval accuracy, tenant isolation, limit enforcement, and error resilience.
