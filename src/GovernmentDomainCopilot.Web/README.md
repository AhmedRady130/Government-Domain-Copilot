# Government Domain Copilot Web

Standalone Angular 18 client for `../API`. It uses standalone components, strict TypeScript DTOs, functional HTTP interceptors, and Angular's default development origin (`http://localhost:4200`). The API already permits that origin in `ApiSecurity:AllowedOrigins`.

## Run locally

```powershell
dotnet run --project ..\API\API.csproj --urls http://localhost:5000
npm install
npm start
```

Open `http://localhost:4200`. The default development API key is the synthetic `gov-key-tenant-a-officer`. Use the supervisor key only when testing approval decisions or execution. Never place production credentials in `environment*.ts`.

The user-facing workspace covers grounded answers, citations/refusals, orchestration including SSE progress, sessions/messages, approvals, hybrid evidence search, document ingestion, runs, and safe LLM trace summaries. It uses only the existing API contracts; the backend has no `api/chat`, profile/identity, Contracts, Compliance, Policy Review, or Audit Log endpoints.

The server has no user-profile endpoint. Consequently the UI does not infer or display a client-selected tenant or role. In development, the synthetic API key is held only in memory. Approval buttons can be attempted by an authenticated user, but the backend's `SupervisorOnly` policy remains the authoritative authorization decision and the UI presents any denial safely.

Text files are converted to the API's JSON `sourceText` contract in the browser. The backend does not expose multipart upload; PDF/DOCX/image text must be extracted before ingestion.
