# ADR 0006: Hybrid Retrieval using Reciprocal Rank Fusion (RRF)

## Status

Accepted

## Context

PR #15 introduced dense vector similarity retrieval using PostgreSQL pgvector. While dense retrieval captures semantic intent, it can omit exact keyword matches (e.g., specific law numbers, decree reference codes, or acronyms). To achieve maximum recall and precision for Government Domain Copilot, retrieval must combine Dense Vector Search and Full-Text Keyword Search into a unified **Hybrid Retrieval** pipeline.

## Decision

1. **Hybrid Retrieval Architecture**:
   - Combine dense vector similarity search (`IChunkRetriever` via pgvector HNSW) and keyword search (`IKeywordChunkRetriever` via PostgreSQL `tsvector`).
   - Orchestrate both branches in `HybridSearchUseCase` using Reciprocal Rank Fusion (`ReciprocalRankFusionService`).

2. **PostgreSQL Full-Text Search Configuration & Constraints**:
   - Use the PostgreSQL `'simple'` text-search configuration (`websearch_to_tsquery('simple', query)`).
   - **SearchVector Scope**: Configured as a stored generated column `"SearchVector" tsvector` on `DocumentChunks` table:
     `to_tsvector('simple', coalesce("Content", ''))`
   - **Table Restriction**: `Document.Title` and `Document.SourceReference` are **not** included in `SearchVector` because PostgreSQL generated columns cannot reference other tables. Cross-table denormalization or trigger-based tsvector aggregation is explicitly deferred to future work.

3. **GIN Index Strategy**:
   - Create a GIN index on `DocumentChunks.SearchVector`:
     `CREATE INDEX IX_DocumentChunks_SearchVector ON "DocumentChunks" USING gin ("SearchVector");`

4. **Reciprocal Rank Fusion (RRF) & Constant**:
   - Use the standard RRF formula:
     $$RRF(d) = \sum_{m \in \text{Channels}} \frac{1}{k + r_m(d)}$$
     with smoothing constant **$k = 60$**.
   - RRF operates purely on ordinal ranks ($1, 2, 3 \dots$), eliminating the need to normalize incompatible raw scores (cosine distance vs ts_rank).
   - Chunks appearing in both branches receive combined scores, ranking them above single-branch candidates. Single-branch candidates remain eligible. Duplicate chunk IDs collapse deterministically.

5. **Candidate Depth Strategy**:
   - Retrieve $2 \times \text{TopK}$ candidates from each branch before fusion, then truncate to the final requested `TopK` (clamped between 1 and 50).

6. **Strict Multi-Tenancy (T0)**:
   - Both vector and keyword queries enforce `TenantId` filtering at the SQL level before ranking or candidate extraction.

7. **Resilient Branch Degradation**:
   - If one search branch fails (e.g., embedding provider timeout), the hybrid service logs a warning and gracefully degrades to the healthy branch, returning valid single-branch RRF results without failing the request.

8. **Known Arabic Language Limitations**:
   - The `'simple'` text-search configuration performs exact token matching without Arabic linguistic stemming or root extraction. Advanced Arabic NLP stemming remains out of scope for this MVP.

## Consequences

- The platform provides robust hybrid dense + keyword search with superior retrieval quality.
- Clean Architecture and multi-tenancy invariants are strictly maintained.
- Unit and PostgreSQL integration tests verify fusion math, dual-match boosting, tenant isolation, and branch failure degradation.
