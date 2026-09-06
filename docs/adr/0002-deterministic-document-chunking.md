# ADR 0002: Deterministic Document Chunking Strategy

## Status

Accepted

## Context

The platform requires splitting incoming government documents into discrete, ordered text chunks prior to persistence and future retrieval. To maintain reproducible ingestion pipelines, auditability, and reliable grounded evidence mapping, chunking must produce identical output for identical input without non-deterministic side effects or external LLM dependencies.

## Decision

Implement `DeterministicDocumentChunker` in Infrastructure configured via `ChunkingOptions` (`ChunkSize` = 1,000 chars default, `ChunkOverlap` = 100 chars default, max bounded by `DocumentChunk.MaxContentLength` = 8,000 chars).

Key rules enforced by the strategy:
1. **Determinism**: Chunking relies on pure, boundary-aware sliding-window logic over normalised text (Unicode NFC, LF line endings). Given the same input and configuration, the chunker always produces identical sequence ordering and content.
2. **Boundary Awareness**: When slicing windows, the algorithm searches backward within the overlap window for natural text boundaries (double newlines, single newlines, sentence punctuation, whitespace) before falling back to exact character length boundaries, preserving semantic cohesion where possible.
3. **No Empty Chunks**: Leading/trailing whitespace is trimmed from each chunk and empty or whitespace-only chunks are discarded.
4. **Contiguous Sequence**: Sequence numbers start at `0` and increment strictly by 1 (`0, 1, 2, ...`).

## Consequences

- Ingestion pipelines behave predictably in test and production environments.
- Chunk content guarantees alignment with the domain invariant (`DocumentChunk.MaxContentLength`).
- Future embeddings and vector indexing (when introduced) will operate on stable, reproducible chunk boundaries.
