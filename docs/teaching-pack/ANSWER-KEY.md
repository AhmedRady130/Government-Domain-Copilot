# Lab Answer Key

## Task 1 — Lockfile and dependency audit

\`package-lock.json\` fixes the resolved npm dependency graph. \`npm ci\` recreates that graph exactly and is therefore the appropriate CI command.

\`\`\`powershell
cd src/Web
npm ci
npm audit --audit-level=low
npm run build
\`\`\`

Do not use \`npm install\` in CI; it can change resolution or rewrite the lockfile.

## Task 2 — Gitleaks workflow solution

The Gitleaks job needs the GitHub token:

\`\`\`yaml
- name: Run Gitleaks
  uses: gitleaks/gitleaks-action@v2
  env:
    GITHUB_TOKEN: \${{ secrets.GITHUB_TOKEN }}
\`\`\`

Full history is required for history scanning:

\`\`\`yaml
- name: Checkout
  uses: actions/checkout@<verified-commit-sha>
  with:
    fetch-depth: 0
\`\`\`

Verify a SHA with an official release page or \`git ls-remote\`; never invent one.

## Task 3 — Verification solution

\`\`\`powershell
dotnet format GovernmentDomainCopilot.sln --verify-no-changes --no-restore
dotnet build GovernmentDomainCopilot.sln --configuration Release --no-restore
dotnet test GovernmentDomainCopilot.sln --configuration Release --no-restore
git diff --check
\`\`\`

If pgvector Testcontainers is unavailable, verify Docker Desktop or a configured test connection. Do not weaken or remove the isolation test.

## Task 4 — PII redaction solution

The policy belongs in Application:

\`\`\`csharp
public interface IPiiRedactor
{
    PiiRedactionResult Redact(string? input);
}
\`\`\`

The endpoint persists only redacted text:

\`\`\`csharp
var piiResult = piiRedactor.Redact(request.SourceText);
var command = new IngestDocumentCommand(
    request.Title ?? string.Empty,
    request.SourceReference ?? string.Empty,
    piiResult.RedactedText);
\`\`\`

Safe audit logging contains counts—not values:

\`\`\`csharp
logger.LogInformation(
    \"Document ingestion redacted PII. EmailCount={EmailCount} PhoneNumberCount={PhoneNumberCount} NationalIdCount={NationalIdCount}\",
    piiResult.EmailCount, piiResult.PhoneNumberCount, piiResult.NationalIdCount);
\`\`\`

A correct test confirms markers remain and source values do not:

\`\`\`csharp
Assert.Contains(\"[REDACTED:EMAIL]\", result.RedactedText);
Assert.DoesNotContain(\"citizen@example.test\", result.RedactedText);
Assert.Equal(1, result.EmailCount);
\`\`\`

## Task 5 — Tenant and prompt safety solution

Vector retrieval must filter before it returns results:

\`\`\`csharp
where chunk.TenantId == tenantId && chunk.Embedding != null
\`\`\`

Evaluation cases should be distinct and require refusal:

\`\`\`csharp
var attacks = cases.Where(c => c.AdversarialType is
    \"PromptInjection\" or \"IndirectPromptInjection\" or \"SecretExfiltration\").ToList();

Assert.True(attacks.Count >= 3);
Assert.All(attacks, item => Assert.True(item.ExpectRefusal));
\`\`\`

Application tenant predicates protect normal application paths. PostgreSQL RLS is still needed as future defense in depth against accidental unscoped access.
