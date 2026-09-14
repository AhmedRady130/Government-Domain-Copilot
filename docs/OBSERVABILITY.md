# Observability and Accountability — FR-9

## Overview

This document describes the observability and accountability features introduced in PR #24 (`feat/observability-fr9`), implementing **FR-9** requirements for the Government Domain Copilot.

---

## Correlation / Request ID

A **Correlation ID** is generated or preserved on every HTTP request and propagates through the entire request pipeline.

### How it works

| Stage | Mechanism |
|---|---|
| HTTP request | `CorrelationIdMiddleware` reads `X-Correlation-ID` header; generates `corr-{uuid}` if absent |
| Scoped context | Sets `ICorrelationContext.CorrelationId` (scoped per request) |
| Orchestrator | `SequentialPipelineOrchestrator` reads from `ICorrelationContext` and sets it on `AgentContext.CorrelationId` |
| Agent execution | `AgentContext.CorrelationId` passes to `GroundedAnswerRequest.CorrelationId` |
| LLM invocation | `GroundedAnswerUseCase` attaches it to `ChatCompletionRequest.CorrelationId` |
| Persistence | Stored in `LlmInvocationTraces.CorrelationId` and `OrchestrationRunTraces.CorrelationId` |
| HTTP response | `X-Correlation-ID` header echoed in every response |

### Configuration

No configuration required. Middleware is registered automatically.

---

## LLM Invocation Tracing

Every LLM chat-completion call (streaming and non-streaming) is recorded as an `LlmInvocationTrace`.

### What is traced

| Field | Description |
|---|---|
| `CorrelationId` | Request correlation ID propagated from HTTP |
| `RunId` | Orchestration run ID (if applicable) |
| `TenantId` | Server-side authenticated tenant (always scoped) |
| `ProviderName` | Provider name (e.g., `Gemini`) |
| `ModelName` | Model name (e.g., `gemini-2.5-flash`) |
| `OperationType` | `ChatCompletion` or `StreamChatCompletion` |
| `StartedAt` / `CompletedAt` | UTC timestamps |
| `Duration` | Wall-clock duration of the LLM call |
| `IsSuccess` | `true` for successful completions, `false` for failures |
| `PromptTokens` / `CompletionTokens` / `TotalTokens` | Token counts from provider usage metadata (nullable) |
| `EstimatedCost` | Estimated cost in USD (nullable; based on `ModelPricing` config) |
| `ErrorMessage` | Sanitized (credentials redacted) error for failure traces |

### LLM invocation paths covered

All chat-completion invocation paths are traced:

1. **`GroundedAnswerUseCase` non-streaming** — `IChatCompletionProvider.CompleteAsync`
2. **`GroundedAnswerUseCase` streaming** — `IChatCompletionProvider.StreamCompleteAsync`
3. **Orchestrator streaming fallback** — falls through to `GroundedAnswerUseCase` with `CorrelationId` + `RunId` propagated
4. **Orchestrator non-streaming fallback** — same path

**Embedding calls are intentionally excluded.** The Gemini and Ollama embedding providers (`GeminiEmbeddingProvider`, `OllamaEmbeddingProvider`) do not return comparable token-usage metadata and are not chat-completion calls. Embedding tracing is out of scope for FR-9.

### Storage

| Environment | Store |
|---|---|
| Production | `PostgresLlmTraceStore` (always) |
| Development | `PostgresLlmTraceStore` (always) |
| Unit tests | `InMemoryLlmTraceStore` (injected via test DI override only) |

> [!CAUTION]
> `InMemoryLlmTraceStore` is **never** registered as a production or development runtime fallback. Tests must explicitly replace `ILlmTraceStore` in their DI configuration.

---

## Token / Cost Accounting

Token counts and estimated costs are recorded when the provider returns usage metadata.

### Default pricing

Configured in `appsettings.json` under `"ModelPricing"`:

```json
"ModelPricing": {
  "PricePerMillionPromptTokens": {
    "gemini-2.5-flash": 0.30,
    "gemini-1.5-flash": 0.075,
    "gemini-1.5-pro": 3.50
  },
  "PricePerMillionCompletionTokens": {
    "gemini-2.5-flash": 2.50,
    "gemini-1.5-flash": 0.30,
    "gemini-1.5-pro": 10.50
  }
}
```

Cost is returned as `null` when:
- No usage metadata is provided by the provider
- The model name is not in the pricing table

---

## Querying Traces

### Endpoint

```
GET /api/traces/llm?correlationId={id}
GET /api/traces/llm?runId={id}
```

**Authentication required.** Results are always scoped to the authenticated tenant.

### Example

```
GET /api/traces/llm?correlationId=corr-abc123
X-Api-Key: <your-api-key>
```

---

## Health and Readiness Endpoints

| Endpoint | Auth | Purpose |
|---|---|---|
| `GET /health` | Anonymous | Liveness probe |
| `GET /ready` | Anonymous | Readiness probe |

---

## Security Properties

- Traces are **always tenant-scoped** — cross-tenant access is rejected at the query level.
- Error messages stored in `LlmInvocationTraces.ErrorMessage` are **sanitized** before persistence: API key patterns and bearer tokens are redacted.
- The correlation ID from request headers is truncated to 100 characters (matching the DB column constraint).
- `X-Tenant-ID` headers are **never** used for tenant resolution; tenant always comes from authenticated claims.

---

## Architecture

- `ICorrelationContext` — Application-layer abstraction (no infra dependency).
- `CorrelationContext` — Infrastructure-layer scoped implementation.
- `ILlmTraceStore` / `ICostCalculator` — Application-layer abstractions.
- `PostgresLlmTraceStore` — Infrastructure-layer EF Core implementation.
- `InMemoryLlmTraceStore` — Application-layer test double (not production).
- `CorrelationIdMiddleware` — API-layer middleware.
- `TraceEndpoints` — API-layer minimal API endpoint.

This preserves Clean Architecture boundaries: Domain and Application layers have zero dependencies on EF Core, ASP.NET Core, or provider SDKs.

---

## Production Deployment Dependency

A PostgreSQL database must be available with the migration `20260914143000_ObservabilityAndLlmTraces` applied. This creates the `LlmInvocationTraces` table and associated indexes.

```bash
dotnet ef database update --project src/Infrastructure --startup-project src/API
```
