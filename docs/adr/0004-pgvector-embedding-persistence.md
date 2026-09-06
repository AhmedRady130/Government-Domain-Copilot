# ADR 0004: PostgreSQL pgvector Embedding Persistence for Document Chunks

## Status

Accepted

## Context

PR #13 introduced `IEmbeddingService` to generate vector embeddings (768 dimensions) for document chunks. To enable downstream vector retrieval and RAG search capabilities in the Government Domain Copilot platform, chunk embeddings must be persisted cleanly in PostgreSQL alongside existing document metadata while maintaining multi-tenancy (T0), Clean Architecture boundaries, and database idempotency.

## Decision

1. **pgvector Extension & EF Core Integration**:
   - Enable PostgreSQL `vector` extension (`modelBuilder.HasPostgresExtension("vector")`).
   - Use `Pgvector.EntityFrameworkCore` (version `0.3.0`) for Npgsql EF Core integration (`npgsqlOptions.UseVector()`).

2. **Domain Model Boundary**:
   - `DocumentChunk` entity contains a nullable property `float[]? Embedding` with a private setter.
   - Domain is completely decoupled from `Pgvector` or `Npgsql` SDK types (uses BCL `float[]` array only).
   - Domain method `SetEmbedding(float[] vector, int expectedDimension)` validates vector non-emptiness and dimension equality (`expectedDimension = 768`) before updating.
   - Null embedding is permitted while a chunk is not yet embedded.

3. **Database Schema & Indexing**:
   - Column `Embedding` on table `DocumentChunks` is configured as type `vector(768)` (nullable).
   - Value conversion converts domain `float[]?` to/from `Pgvector.Vector?`.
   - Add an **HNSW index** with **cosine distance** (`vector_cosine_ops`):
     - `CREATE INDEX IX_DocumentChunks_Embedding ON "DocumentChunks" USING hnsw ("Embedding" vector_cosine_ops);`
   - **Rationale**: HNSW provides sub-linear approximate nearest-neighbor query performance with high recall and low latency. Cosine distance matches unit-normalized embedding models (e.g. Gemini `gemini-embedding-2` with 768 dimensions).

4. **Persistence Abstraction & Multi-Tenancy (T0)**:
   - Introduce `IChunkEmbeddingRepository` in the Application layer.
   - Extend `DocumentRepository` in Infrastructure to implement `IChunkEmbeddingRepository`.
   - Method `PersistEmbeddingsAsync` is strictly tenant-scoped (`tenantId` parameter):
     - Validates that all target chunk IDs exist and belong to `tenantId`.
     - Rejects any cross-tenant chunk update attempt with `InvalidOperationException`.
     - Updates chunk embeddings in memory, calling `SetEmbedding`, and executes `SaveChangesAsync` atomically.

5. **Application Embedding Orchestration**:
   - Introduce `IChunkEmbeddingService` / `ChunkEmbeddingService` in Application layer to bridge `IEmbeddingService` generation and `IChunkEmbeddingRepository` persistence.
   - Embeddings are updated idempotently on existing `DocumentChunk` records without creating duplicate rows.

6. **Observability & Safe Logging**:
   - Vector values are classified as high-volume data and are **never logged**.
   - Safe metadata logged includes: chunk count, tenant ID, provider name, model, dimension, and execution duration.

## Consequences

- Chunk embeddings are safely persisted in PostgreSQL using native pgvector `vector(768)` type.
- HNSW index ensures optimal performance for future vector search queries.
- Clean Architecture and T0 multi-tenancy invariants are strictly maintained.
- Unit and integration tests verify dimension safety, idempotency, and cross-tenant write rejection.
