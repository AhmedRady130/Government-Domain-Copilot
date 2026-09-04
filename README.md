# Government Domain Copilot

Foundation for an agentic retrieval-augmented-generation platform in a government domain.

## Structure

- `src/Domain` — core domain layer; framework and infrastructure independent.
- `src/Application` — application use-case and abstraction layer; depends only on Domain.
- `src/Infrastructure` — future external-service and persistence implementations; depends on Application and Domain.
- `src/API` — ASP.NET Core host; depends on Application and Infrastructure.
- `src/Web` — Angular web application.
- `tests` — unit, integration, and contract test projects.
- `docs/adr` — architecture decision records.

## Prerequisites

- .NET SDK 9
- Node.js and npm

## Verification

```powershell
dotnet build .\GovernmentDomainCopilot.sln
dotnet test .\GovernmentDomainCopilot.sln
Set-Location .\src\Web
npm run build
```

No government-domain requirements, data, or application features are included in this initial foundation.
