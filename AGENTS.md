# Government Domain Copilot — Agent Guidance

## Scope and architecture

- Preserve Clean Architecture dependency flow: `Domain` has no infrastructure, web, data-access, AI, or framework dependencies; `Application` depends only on `Domain`; `Infrastructure` depends on `Application` and `Domain`; `API` depends on `Application` and `Infrastructure`.
- Introduce external AI, LLM, embedding, and vector-store capabilities only behind application-layer abstractions.
- Define explicit allow-lists for future agent tools. Side-effecting tools require human approval before execution.
- Do not invent government requirements, facts, policies, or data.

## Engineering safeguards

- Never add secrets, credentials, tokens, or connection strings to source control. Use local environment configuration and keep `.env` files untracked.
- Add or update tests for every new behavior.
- Keep this foundation free of RAG, embeddings, vector search, agents, orchestration, authentication, multi-tenancy implementation, business workflows, database entities, and LLM integrations until explicitly requested.
