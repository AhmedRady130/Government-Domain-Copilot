# ADR 0003: Embedding Provider Abstraction and Fallback Strategy

## Status

Accepted

## Context

The Government Domain Copilot platform requires generating vector embeddings for document text chunks during ingestion and vector search workflows. To prevent vendor lock-in, preserve Clean Architecture boundaries, and ensure service availability under rate limits or external API outages, embedding generation must be decoupled from specific cloud SDKs or single provider implementations.

## Decision

1. **Clean Architecture Boundary (`Application` Layer)**:
   - Define a provider-independent `IEmbeddingProvider` abstraction and typed request/result models (`EmbeddingRequest`, `EmbeddingResult`, `EmbeddingItem`) in the `Application` layer.
   - The `Application` layer contains zero vendor SDK dependencies (no OpenAI, Azure, Google SDKs, or Semantic Kernel).

2. **Dual Provider Adapters (`Infrastructure` Layer)**:
   - Implement **Primary Provider Adapter**: `GeminiEmbeddingProvider` using Google Gemini API (`gemini-embedding-2`, configured to 768 output dimensions via `outputDimensionality` parameter using Matryoshka Representation Learning, superseding legacy `text-embedding-004`).
   - Implement **Alternative/Local Provider Adapter**: `OllamaEmbeddingProvider` using local Ollama service (`nomic-embed-text`, default 768 dimensions).
   - All HTTP transport and REST serialization details reside strictly inside `Infrastructure`.

3. **Resilient Fallback Service (`ResilientEmbeddingService`)**:
   - Implement `IEmbeddingService` to handle primary provider invocation with automatic fallback routing.
   - When the primary provider encounters transient or availability failures (`EmbeddingProviderUnavailableException`, `EmbeddingRateLimitException`), the service logs a safe warning and routes the request to the configured fallback provider.
   - Deterministic errors (validation failures, invalid input, batch size overflow, dimension mismatch) do **not** trigger fallback and are raised immediately.

4. **Strict Dimension Validation**:
   - Every embedding vector is validated against the configured `ExpectedDimensions` (768). Incompatible dimension sizes trigger `EmbeddingDimensionMismatchException` to prevent storing incompatible vectors.

5. **Multi-Tenancy & Security Invariants**:
   - Vectors contain pure numeric floating-point values and never include `TenantId` or user metadata.
   - Credentials (API keys, authorization headers) are sourced from environment variables or configuration and are never logged or committed.

## Consequences

- The platform can swap or fallback between hosted (Gemini) and local (Ollama) embedding providers seamlessly without application code changes.
- Vector generation remains fully testable via provider test doubles and HTTP stubs.
- Vector storage (pgvector) and search workflows can consume `IEmbeddingService` cleanly in future PRs.
