# ADR 0007: Deterministic Retrieval Reranking using Weighted Signal Fusion

## Status

Accepted

## Context

PR #16 introduced hybrid retrieval using Reciprocal Rank Fusion (RRF) combining dense vector search and full-text keyword search. RRF operates purely on ordinal ranks, discarding the absolute magnitude of retrieval signals (cosine distance and ts_rank). This means two chunks with the same RRF rank position are treated identically, even if one has a far superior vector closeness score or keyword relevance score.

FR-2 requires one documented retrieval enhancement. Adding a deterministic reranking stage after RRF fusion allows the system to incorporate the raw retrieval signal magnitudes into the final ordering, providing a strictly better-informed ranking without requiring an LLM, network call, or additional database query.

## Decision

Add a **deterministic reranking stage** (`WeightedSignalReranker`) immediately after RRF fusion in `HybridSearchUseCase`, operating over the already-bounded, tenant-scoped candidate set.

### Algorithm: Weighted Signal Fusion

Each fused candidate receives a `RerankScore` computed from three normalized signals:

```
RerankScore = w_rrf   × NormRrfScore
            + w_vec   × VectorCloseness
            + w_kw    × NormKeywordScore
```

### Signal Normalization

| Signal | Formula | Range | Missing value |
|---|---|---|---|
| `NormRrfScore` | `clamp(RrfScore / max(RrfScore in candidate set), 0, 1)` | [0, 1] | Not possible (always present) |
| `VectorCloseness` | `clamp(1 − Distance / 2.0, 0, 1)` | [0, 1] | Distance=null (keyword-only chunk) → **0.0** |
| `NormKeywordScore` | `clamp(KeywordScore / max(KeywordScore in candidate set), 0, 1)` | [0, 1] | KeywordScore=null (vector-only chunk) → **0.0** |

- **Cosine distance** is in [0, 2] (pgvector range). Dividing by the constant 2 and clamping to [0, 1] produces safe bounds with no divide-by-zero risk.
- **RRF score** and **keyword score** are normalized within the candidate set and clamped to [0, 1]. If all values are zero (degenerate input), normalized values remain 0 — no divide-by-zero, original ordering preserved by tie-breaking.

### Weights (MVP Heuristics)

| Weight | Value | Rationale |
|---|---|---|
| `w_rrf` (RRF) | **0.50** | RRF is the primary fusion signal already aggregating rank evidence from both retrieval branches. Given the largest single weight. |
| `w_vec` (Vector) | **0.30** | Semantic similarity is strongly predictive for government document relevance where question intent must match policy content. |
| `w_kw` (Keyword) | **0.20** | Keyword precision supplements semantic search and is especially useful for exact references (law numbers, decree codes) but can match stopwords; given less weight. |

> **Important**: These weights are **MVP heuristics only**. They are not derived from an offline evaluation corpus with labelled relevance judgements. Future work should replace them with empirically tuned or learned weights.

### Tie-Breaking (deterministic)

When two candidates have equal `RerankScore`, ordering is resolved by:

1. `RerankScore` DESC *(primary)*
2. `RrfScore` DESC *(secondary)*
3. Pre-rerank `Rank` ASC *(tertiary — preserves upstream ordering)*
4. `ChunkId` ASC *(quaternary — lexicographic Guid, arbitrary but stable)*

This guarantees identical output for identical input across all runs.

### Tenant Isolation

The reranker receives only candidates already filtered to the authenticated tenant's scope:

- Vector search is tenant-scoped at the SQL level (`WHERE TenantId = @tenantId`)
- Keyword search is tenant-scoped at the SQL level (`WHERE TenantId = @tenantId`)
- RRF fuses only tenant-scoped candidates
- The reranker receives only fused tenant-scoped candidates
- No cross-tenant cache, no global candidate pool

`TenantId` is resolved from `ITenantContext` in `HybridSearchUseCase`, not from the API request body.

### Pipeline

```
User Query
    ├── Dense/Vector Retrieval (2 × TopK, tenant-scoped SQL)
    └── Keyword Retrieval (2 × TopK, tenant-scoped SQL)
              ↓
        RRF Fusion (k=60, ReciprocalRankFusionService)
              ↓
        Deterministic Reranking (WeightedSignalReranker)
              ↓
        Final Top-K Results
```

### Observability

`HybridSearchUseCase` logs:
- Reranker name/version (`WeightedSignalReranker-v1`)
- Candidate count entering reranker
- Final count after TopK slice
- Total duration (including reranking)

Chunk content, query text, and vector values are **not** logged.

### API Contract (backwards-compatible)

The API response (`SearchResultItemApiResponse`) gains two new **additive** fields:
- `rerankScore` — the reranking score (always present)
- `finalRank` — 1-based post-rerank position (always present)

The existing `rank` field (RRF rank before reranking) is preserved unchanged for diagnostics transparency.

## Alternatives Considered

| Alternative | Reason Rejected |
|---|---|
| Cross-encoder LLM reranking | Requires an LLM call per query, adds latency and cost, introduces non-determinism — out of scope for FR-2 MVP |
| BM25 score normalization only | Does not incorporate vector similarity signal, leaving semantic precision on the table |
| Dropping RRF in favour of reranking alone | RRF provides robust multi-branch fusion; reranking complements rather than replaces it |

## Limitations

- Weights are MVP heuristics, not benchmarked against a labelled government document evaluation corpus.
- `VectorCloseness = 0.0` for keyword-only chunks slightly disadvantages them vs. chunks with both signals, even when their keyword relevance is extremely high.
- The `'simple'` text-search configuration (from ADR-0006) means Arabic text may not be stemmed; this affects `KeywordScore` values for Arabic queries.
- No online A/B testing or feedback loop is implemented.

## Future Work

- Replace heuristic weights with empirically tuned or learned values via offline evaluation.
- Optionally replace `WeightedSignalReranker` with a cross-encoder or LLM-based reranker behind `IRetrievalReranker`, with no changes to the pipeline.
- Add relevance feedback or implicit signal collection to support evaluation.

## Consequences

- The pipeline produces a more informed final ranking that incorporates raw signal magnitudes beyond ordinal ranks.
- No new packages, no schema migration, no additional I/O.
- Clean Architecture maintained: reranker is a pure Application-layer service.
- Multi-tenancy invariants are strictly preserved at every stage.
- Unit and integration tests verify determinism, normalization correctness, signal handling, and tenant isolation.
