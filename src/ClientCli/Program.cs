// Government Domain Copilot — Operator & Developer CLI
// Composes existing Application and Infrastructure services via DI.
// Enforces server-side tenant isolation through ITenantContext without client bypass.

using GovernmentDomainCopilot.Application;
using GovernmentDomainCopilot.Application.Abstractions;
using GovernmentDomainCopilot.Application.Agents.Abstractions;
using GovernmentDomainCopilot.Application.Agents.Models;
using GovernmentDomainCopilot.Application.Answering.Abstractions;
using GovernmentDomainCopilot.Application.Answering.Models;
using GovernmentDomainCopilot.Application.Corpus;
using GovernmentDomainCopilot.Application.Documents;
using GovernmentDomainCopilot.Application.Documents.Commands;
using GovernmentDomainCopilot.Application.Sessions.Abstractions;
using GovernmentDomainCopilot.Application.Traces.Abstractions;
using GovernmentDomainCopilot.Infrastructure.Configuration;
using GovernmentDomainCopilot.Infrastructure;
using GovernmentDomainCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// --- Argument Validation: Reject spoofed tenant-id flags ---
if (args.Any(a => a.Equals("--tenant-id", StringComparison.OrdinalIgnoreCase) || a.StartsWith("--tenant-id=", StringComparison.OrdinalIgnoreCase)))
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine("Error: Arbitrary --tenant-id parameter is forbidden. Tenant identity must come exclusively from authenticated credentials.");
    Console.ResetColor();
    return 1;
}

// --- Help Display ---
if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
{
    PrintHelp();
    return 0;
}

var command = args[0].ToLowerInvariant();
var rawCommandArgs = args.Skip(1).ToArray();

// Validation is intentionally a pure filesystem operation. It must not require
// authentication configuration, EF Core, or a database connection.
if (command == "validate-corpus")
{
    return HandleValidateCorpus();
}

// --- FR-8: API Key Authentication (Development/Test synthetic identities) ---
string? cliApiKey = null;
var commandArgsList = new List<string>();
for (int i = 0; i < rawCommandArgs.Length; i++)
{
    if (rawCommandArgs[i] == "--api-key" && i + 1 < rawCommandArgs.Length)
    {
        cliApiKey = rawCommandArgs[++i];
    }
    else
    {
        commandArgsList.Add(rawCommandArgs[i]);
    }
}
var commandArgs = commandArgsList.ToArray();

GovernmentDomainCopilot.Infrastructure.Auth.TestUserRecord? identity = null;
if (cliApiKey != null)
{
    identity = GovernmentDomainCopilot.Infrastructure.Auth.SeedAuthIdentities.FindByApiKey(cliApiKey);
    if (identity == null)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine("Authentication failed: invalid or unrecognized API key.");
        Console.ResetColor();
        return 1;
    }
    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine($"Authenticated as: {identity.DisplayName} [{identity.Role}] (Tenant: {identity.TenantId})");
    Console.ResetColor();
}
else if (command != "seed-corpus")
{
    Console.ForegroundColor = ConsoleColor.DarkYellow;
    Console.WriteLine("Notice: Running in Development/Test mode with default development tenant context. (Use --api-key for authenticated user).");
    Console.ResetColor();
}

// --- FR-8: Server-side Role Authorization Gate for CLI ---
if (command is "approve" or "reject" or "execute-approval")
{
    if (identity == null ||
        !string.Equals(
            identity.Role,
            GovernmentDomainCopilot.Domain.Constants.Roles.Supervisor,
            StringComparison.OrdinalIgnoreCase))
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine(
            $"Access Denied: The '{command}' command requires an authenticated '{GovernmentDomainCopilot.Domain.Constants.Roles.Supervisor}'.");
        Console.ResetColor();
        return 1;
    }
}

if (command == "seed-corpus" && identity is null)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.Error.WriteLine("Authentication required: 'seed-corpus' requires an authenticated API-key identity.");
    Console.ResetColor();
    return 1;
}

// --- Configuration & DI ---
var inMemoryConfig = new Dictionary<string, string?>
{
    ["Tenant:DevelopmentTenantId"] = identity?.TenantId.ToString() ?? "11111111-1111-1111-1111-111111111111",
    ["Logging:LogLevel:Default"] = "Warning"
};

var configuration = new ConfigurationBuilder()
    .AddInMemoryCollection(inMemoryConfig)
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var services = new ServiceCollection();
services.AddSingleton<IConfiguration>(configuration);
services.AddSingleton<IHostEnvironment>(new CliHostEnvironment(
    Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
    ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
    ?? Environments.Development));
services.AddLogging(b =>
{
    b.AddConsole();
    b.SetMinimumLevel(LogLevel.Warning);
});

services.AddApplication();
var useInMemoryDatabase = !DatabaseConnectionConfiguration.HasConfiguredConnectionString(configuration);
services.AddInfrastructure(
    configuration,
    useInMemoryDatabase
        ? options => options.UseInMemoryDatabase("GovernmentDomainCopilot_Cli")
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
        : null);

if (identity != null)
{
    var existingTenantContext = services.FirstOrDefault(d => d.ServiceType == typeof(ITenantContext));
    if (existingTenantContext != null) services.Remove(existingTenantContext);

    var existingUserContext = services.FirstOrDefault(d => d.ServiceType == typeof(ICurrentUserContext));
    if (existingUserContext != null) services.Remove(existingUserContext);

    services.AddScoped<ICurrentUserContext>(_ => new CliUserContext(identity));
    services.AddScoped<ITenantContext>(_ => new CliTenantContext(identity.TenantId));
}

var serviceProvider = services.BuildServiceProvider();

// Pre-seed tenant and user in DbContext for the active tenant context
using (var scope = serviceProvider.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<GovernmentDomainCopilotDbContext>();
    var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
    var tenantId = tenantContext.GetTenantId();

    if (!db.Tenants.Any(t => t.Id == tenantId))
    {
        db.Tenants.Add(new GovernmentDomainCopilot.Domain.Entities.Tenant(tenantId, $"Tenant {tenantId}", DateTimeOffset.UtcNow));
        db.Users.Add(new GovernmentDomainCopilot.Domain.Entities.User(
            identity?.UserId ?? Guid.NewGuid(),
            tenantId,
            identity?.ExternalId ?? "cli-operator",
            identity?.DisplayName ?? "CLI Operator",
            DateTimeOffset.UtcNow,
            identity?.Role ?? "Officer"));
        db.SaveChanges();
    }
}

try
{
    using var scope = serviceProvider.CreateScope();
    var sp = scope.ServiceProvider;

    switch (command)
    {
        case "ingest":
            return await HandleIngestAsync(sp, commandArgs);

        case "seed-corpus":
            return await HandleSeedCorpusAsync(sp);

        case "ask":
            return await HandleAskAsync(sp, commandArgs);

        case "run":
            return await HandleRunAsync(sp, commandArgs);

        case "runs":
            return await HandleListRunsAsync(sp, commandArgs);

        case "trace":
        case "run-inspect":
            return await HandleTraceAsync(sp, commandArgs);

        case "sessions":
            return await HandleListSessionsAsync(sp, commandArgs);

        case "session":
            return await HandleGetSessionAsync(sp, commandArgs);

        case "session-create":
            return await HandleCreateSessionAsync(sp, commandArgs);

        case "approvals":
            return await HandleListApprovalsAsync(sp, commandArgs);

        case "approval":
            return await HandleGetApprovalAsync(sp, commandArgs);

        case "approve":
            return await HandleApproveAsync(sp, commandArgs);

        case "reject":
            return await HandleRejectAsync(sp, commandArgs);

        case "execute-approval":
            return await HandleExecuteApprovalAsync(sp, commandArgs);

        default:
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Unknown command: '{command}'");
            Console.ResetColor();
            Console.WriteLine("Run with --help to view available commands.");
            return 1;
    }
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"Error: {ex.Message}");
    Console.ResetColor();
    return 1;
}

// ==========================================
// Handlers
// ==========================================

static async Task<int> HandleIngestAsync(IServiceProvider sp, string[] args)
{
    string? title = null;
    string? sourceReference = null;
    string? text = null;
    string? filePath = null;

    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == "--title" && i + 1 < args.Length) title = args[++i];
        else if (args[i] == "--ref" && i + 1 < args.Length) sourceReference = args[++i];
        else if (args[i] == "--text" && i + 1 < args.Length) text = args[++i];
        else if (args[i] == "--file" && i + 1 < args.Length) filePath = args[++i];
    }

    if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(sourceReference))
    {
        Console.WriteLine("Usage: ingest --title <title> --ref <sourceReference> (--text <text> | --file <filePath>)");
        return 1;
    }

    if (string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(filePath))
    {
        if (!File.Exists(filePath))
        {
            Console.WriteLine($"File not found: {filePath}");
            return 1;
        }
        text = await File.ReadAllTextAsync(filePath);
    }

    if (string.IsNullOrWhiteSpace(text))
    {
        Console.WriteLine("Error: Either --text or --file must be provided with non-empty content.");
        return 1;
    }

    var useCase = sp.GetRequiredService<IIngestDocumentUseCase>();
    var result = await useCase.IngestAsync(new IngestDocumentCommand(title, sourceReference, text), CancellationToken.None);

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"Document Ingested: {result.DocumentId}");
    Console.ResetColor();
    Console.WriteLine($"Status:      {result.Status}");
    Console.WriteLine($"Chunk Count: {result.ChunkCount}");
    return 0;
}

static int HandleValidateCorpus()
{
    var corpusDirectory = FindCorpusDirectory();
    var manifest = CorpusManifest.Parse(File.ReadAllText(Path.Combine(corpusDirectory, "manifest.json")));
    var validation = CorpusValidator.Validate(manifest, relativeFile => File.ReadAllText(Path.Combine(corpusDirectory, relativeFile)));
    Console.WriteLine($"Documents: {validation.DocumentCount}");
    Console.WriteLine($"Pages: {validation.PageCount}");
    Console.WriteLine("Tenants: " + string.Join(", ", validation.TenantDistribution.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}")));
    Console.WriteLine("Formats: " + string.Join(", ", validation.FormatDistribution.Select(p => $"{p.Key}={p.Value}")));
    if (validation.IsValid)
    {
        Console.WriteLine("Corpus validation: PASS");
        return 0;
    }
    foreach (var error in validation.Errors) Console.Error.WriteLine("ERROR: " + error);
    return 1;
}

static async Task<int> HandleSeedCorpusAsync(IServiceProvider sp)
{
    if (HandleValidateCorpus() != 0) return 1;
    var corpusDirectory = FindCorpusDirectory();
    var manifest = CorpusManifest.Parse(await File.ReadAllTextAsync(Path.Combine(corpusDirectory, "manifest.json")));
    var tenantId = sp.GetRequiredService<ITenantContext>().GetTenantId().ToString();
    var documents = manifest.Documents.Where(d => string.Equals(d.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)).ToList();
    if (documents.Count == 0)
    {
        Console.Error.WriteLine("No corpus documents are assigned to the authenticated tenant.");
        return 1;
    }

    var useCase = sp.GetRequiredService<IIngestDocumentUseCase>();
    var failures = 0;
    foreach (var document in documents)
    {
        try
        {
            var content = await File.ReadAllTextAsync(Path.Combine(corpusDirectory, document.File));
            var result = await useCase.IngestAsync(new IngestDocumentCommand(document.Title, document.SourceReference, content), CancellationToken.None);
            Console.WriteLine($"[OK] {document.Id}: {result.Status}, {result.ChunkCount} chunks");
        }
        catch (Exception ex)
        {
            failures++;
            Console.Error.WriteLine($"[FAIL] {document.Id}: {ex.Message}");
        }
    }
    Console.WriteLine($"Seed complete: {documents.Count - failures} succeeded, {failures} failed for authenticated tenant {tenantId}.");
    return failures == 0 ? 0 : 1;
}

static string FindRepositoryRoot()
{
    for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory != null; directory = directory.Parent)
        if (File.Exists(Path.Combine(directory.FullName, "GovernmentDomainCopilot.sln"))) return directory.FullName;
    throw new DirectoryNotFoundException("Could not find repository root containing GovernmentDomainCopilot.sln.");
}

static string FindCorpusDirectory()
{
    var publishedCorpusDirectory = Path.Combine(AppContext.BaseDirectory, "data", "corpus");
    if (File.Exists(Path.Combine(publishedCorpusDirectory, "manifest.json"))) return publishedCorpusDirectory;

    return Path.Combine(FindRepositoryRoot(), "data", "corpus");
}

static async Task<int> HandleAskAsync(IServiceProvider sp, string[] args)
{
    string? query = null;
    int? topK = null;
    string? sessionId = null;

    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == "--query" && i + 1 < args.Length) query = args[++i];
        else if (args[i] == "--top-k" && i + 1 < args.Length && int.TryParse(args[++i], out var k)) topK = k;
        else if (args[i] == "--session-id" && i + 1 < args.Length) sessionId = args[++i];
        else if (query == null && !args[i].StartsWith("--")) query = args[i];
    }

    if (string.IsNullOrWhiteSpace(query))
    {
        Console.WriteLine("Usage: ask \"<query>\" [--top-k <k>] [--session-id <id>]");
        return 1;
    }

    var tenantContext = sp.GetRequiredService<ITenantContext>();
    var tenantId = tenantContext.GetTenantId();
    var sessionStore = sp.GetRequiredService<ISessionStore>();

    if (!string.IsNullOrWhiteSpace(sessionId))
    {
        var session = await sessionStore.GetSessionAsync(sessionId, tenantId);
        if (session == null)
        {
            Console.WriteLine($"Error: Session '{sessionId}' was not found for the authenticated tenant.");
            return 1;
        }

        await sessionStore.AppendMessageAsync(sessionId, tenantId, "user", query, "UserQuery");
    }

    var useCase = sp.GetRequiredService<IGroundedAnswerUseCase>();
    var result = await useCase.GetGroundedAnswerAsync(new GroundedAnswerRequest(query, topK), CancellationToken.None);

    Console.WriteLine();
    Console.WriteLine($"Status:   {result.Status}");
    Console.WriteLine($"Provider: {result.ProviderName} ({result.ModelName})");
    Console.WriteLine();

    if (result.Status == GroundedAnswerStatus.Grounded)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("ANSWER:");
        Console.ResetColor();
        Console.WriteLine(result.Answer);
        Console.WriteLine();

        if (result.Citations.Count > 0)
        {
            Console.WriteLine("CITATIONS:");
            foreach (var c in result.Citations)
            {
                Console.WriteLine($"  [{c.Sequence}] {c.Title} ({c.SourceReference})");
            }
        }
    }
    else
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("REFUSAL REASON:");
        Console.ResetColor();
        Console.WriteLine(result.Reason);
    }

    if (!string.IsNullOrWhiteSpace(sessionId))
    {
        var answerText = result.Answer ?? result.Reason ?? "No response generated.";
        await sessionStore.AppendMessageAsync(sessionId, tenantId, "assistant", answerText, result.Status.ToString(), result.Citations);
        Console.WriteLine();
        Console.WriteLine($"(Recorded in session: {sessionId})");
    }

    return 0;
}

static async Task<int> HandleRunAsync(IServiceProvider sp, string[] args)
{
    string? query = null;
    string? correlationId = null;
    string? sessionId = null;

    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == "--query" && i + 1 < args.Length) query = args[++i];
        else if (args[i] == "--correlation-id" && i + 1 < args.Length) correlationId = args[++i];
        else if (args[i] == "--session-id" && i + 1 < args.Length) sessionId = args[++i];
        else if (query == null && !args[i].StartsWith("--")) query = args[i];
    }

    if (string.IsNullOrWhiteSpace(query))
    {
        Console.WriteLine("Usage: run \"<query>\" [--correlation-id <id>] [--session-id <id>]");
        return 1;
    }

    var orchestrator = sp.GetRequiredService<IMultiAgentOrchestrator>();
    var runRecord = await orchestrator.OrchestrateAsync(query, correlationId, sessionId);

    Console.WriteLine();
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"RUN ID:      {runRecord.RunId}");
    Console.ResetColor();
    Console.WriteLine($"Status:      {runRecord.Status}");
    Console.WriteLine($"Pattern:     {runRecord.PatternName}");
    Console.WriteLine($"Iterations:  {runRecord.IterationCount}");
    Console.WriteLine($"Duration:    {runRecord.Duration.TotalMilliseconds:F1}ms");
    if (runRecord.SessionId != null) Console.WriteLine($"SessionId:   {runRecord.SessionId}");

    Console.WriteLine();
    Console.WriteLine("AGENT EXECUTIONS:");
    foreach (var agent in runRecord.AgentExecutions)
    {
        var mark = agent.Success ? "[OK]" : "[FAIL]";
        Console.WriteLine($"  {mark} {agent.AgentRole,-30} ({agent.Duration.TotalMilliseconds:F0}ms) -> {agent.OutputSummary}");
        foreach (var tc in agent.ToolCalls)
        {
            Console.WriteLine($"       Tool: {tc.ToolName,-25} Success={tc.Success} ({tc.Duration.TotalMilliseconds:F0}ms)");
        }
    }

    if (runRecord.PendingApproval != null)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"APPROVAL REQUIRED: RequestId={runRecord.PendingApproval.RequestId} Action={runRecord.PendingApproval.ProposedAction}");
        Console.ResetColor();
    }

    Console.WriteLine();
    if (runRecord.FinalResponse?.Answer != null)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("FINAL ANSWER:");
        Console.ResetColor();
        Console.WriteLine(runRecord.FinalResponse.Answer);

        if (runRecord.FinalResponse.Citations.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("CITATIONS:");
            foreach (var c in runRecord.FinalResponse.Citations)
            {
                Console.WriteLine($"  [{c.Sequence}] {c.Title} ({c.SourceReference})");
            }
        }
    }
    else if (runRecord.FailureReason != null)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"FAILURE: {runRecord.FailureReason}");
        Console.ResetColor();
    }

    return 0;
}

static async Task<int> HandleListRunsAsync(IServiceProvider sp, string[] args)
{
    int limit = 20;
    string? sessionId = null;

    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == "--limit" && i + 1 < args.Length && int.TryParse(args[++i], out var lim)) limit = lim;
        else if (args[i] == "--session-id" && i + 1 < args.Length) sessionId = args[++i];
    }

    var tenantContext = sp.GetRequiredService<ITenantContext>();
    var tenantId = tenantContext.GetTenantId();
    var traceStore = sp.GetRequiredService<IRunTraceStore>();

    var runs = await traceStore.ListRunsAsync(tenantId, 0, limit, sessionId);

    Console.WriteLine($"Orchestration Runs ({runs.Count}):");
    Console.WriteLine("--------------------------------------------------------------------------------------------------");
    Console.WriteLine($"{"RunId",-36} {"Status",-14} {"Duration",-10} {"StartedAt (UTC)",-22} {"SessionId"}");
    Console.WriteLine("--------------------------------------------------------------------------------------------------");

    foreach (var r in runs)
    {
        Console.WriteLine($"{r.RunId,-36} {r.Status,-14} {r.Duration.TotalMilliseconds,8:F0}ms {r.StartedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"),-22} {r.SessionId ?? "-"}");
    }

    return 0;
}

static async Task<int> HandleTraceAsync(IServiceProvider sp, string[] args)
{
    string? runId = null;

    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == "--run-id" && i + 1 < args.Length) runId = args[++i];
        else if (runId == null && !args[i].StartsWith("--")) runId = args[i];
    }

    if (string.IsNullOrWhiteSpace(runId))
    {
        Console.WriteLine("Usage: trace <runId>");
        return 1;
    }

    var tenantContext = sp.GetRequiredService<ITenantContext>();
    var tenantId = tenantContext.GetTenantId();
    var traceStore = sp.GetRequiredService<IRunTraceStore>();

    var run = await traceStore.GetRunAsync(runId, tenantId);
    if (run == null)
    {
        Console.WriteLine($"Run '{runId}' was not found for the authenticated tenant.");
        return 1;
    }

    Console.WriteLine("================================================================================");
    Console.WriteLine($"RUN TRACE: {run.RunId}");
    Console.WriteLine("================================================================================");
    Console.WriteLine($"Tenant:        {run.TenantId}");
    Console.WriteLine($"Correlation:   {run.CorrelationId}");
    Console.WriteLine($"Status:        {run.Status}");
    Console.WriteLine($"Pattern:       {run.PatternName}");
    Console.WriteLine($"StartedAt:     {run.StartedAt:u}");
    Console.WriteLine($"CompletedAt:   {run.CompletedAt:u}");
    Console.WriteLine($"Duration:      {run.Duration.TotalMilliseconds:F1}ms");
    Console.WriteLine($"Iterations:    {run.IterationCount}");
    Console.WriteLine($"UsedFallback:  {run.UsedFallback}");
    if (run.SessionId != null) Console.WriteLine($"SessionId:     {run.SessionId}");
    if (run.FailureReason != null) Console.WriteLine($"FailureReason: {run.FailureReason}");

    Console.WriteLine();
    Console.WriteLine("AGENT EXECUTIONS:");
    foreach (var a in run.AgentExecutions)
    {
        Console.WriteLine($"  - {a.AgentRole}: Success={a.Success} Duration={a.Duration.TotalMilliseconds:F0}ms");
        Console.WriteLine($"    Summary: {a.OutputSummary}");
        if (a.ErrorMessage != null) Console.WriteLine($"    Error:   {a.ErrorMessage}");
        foreach (var tc in a.ToolCalls)
        {
            Console.WriteLine($"    Tool: {tc.ToolName} Success={tc.Success} ({tc.Duration.TotalMilliseconds:F0}ms)");
        }
    }

    if (run.PendingApproval != null)
    {
        var apr = run.PendingApproval;
        Console.WriteLine();
        Console.WriteLine("APPROVAL REQUEST:");
        Console.WriteLine($"  RequestId: {apr.RequestId} Action={apr.ProposedAction} Decision={apr.Decision}");
    }

    if (run.FinalResponse != null)
    {
        Console.WriteLine();
        Console.WriteLine($"FINAL ANSWER (Status={run.FinalResponse.Status}):");
        Console.WriteLine(run.FinalResponse.Answer ?? run.FinalResponse.Reason);
    }

    return 0;
}

static async Task<int> HandleListSessionsAsync(IServiceProvider sp, string[] args)
{
    int limit = 20;
    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == "--limit" && i + 1 < args.Length && int.TryParse(args[++i], out var lim)) limit = lim;
    }

    var tenantContext = sp.GetRequiredService<ITenantContext>();
    var tenantId = tenantContext.GetTenantId();
    var sessionStore = sp.GetRequiredService<ISessionStore>();

    var sessions = await sessionStore.ListSessionsAsync(tenantId, 0, limit);

    Console.WriteLine($"Conversation Sessions ({sessions.Count}):");
    Console.WriteLine("--------------------------------------------------------------------------------------------------");
    Console.WriteLine($"{"SessionId",-36} {"Status",-10} {"Last Activity (UTC)",-22} {"Title"}");
    Console.WriteLine("--------------------------------------------------------------------------------------------------");

    foreach (var s in sessions)
    {
        Console.WriteLine($"{s.SessionId,-36} {s.Status,-10} {s.LastActivityAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"),-22} {s.Title}");
    }

    return 0;
}

static async Task<int> HandleGetSessionAsync(IServiceProvider sp, string[] args)
{
    string? sessionId = null;
    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == "--session-id" && i + 1 < args.Length) sessionId = args[++i];
        else if (sessionId == null && !args[i].StartsWith("--")) sessionId = args[i];
    }

    if (string.IsNullOrWhiteSpace(sessionId))
    {
        Console.WriteLine("Usage: session <sessionId>");
        return 1;
    }

    var tenantContext = sp.GetRequiredService<ITenantContext>();
    var tenantId = tenantContext.GetTenantId();
    var sessionStore = sp.GetRequiredService<ISessionStore>();

    var session = await sessionStore.GetSessionAsync(sessionId, tenantId);
    if (session == null)
    {
        Console.WriteLine($"Session '{sessionId}' was not found for the authenticated tenant.");
        return 1;
    }

    var messages = await sessionStore.GetMessagesAsync(sessionId, tenantId) ?? new List<GovernmentDomainCopilot.Application.Sessions.Models.SessionMessageRecord>();

    Console.WriteLine("================================================================================");
    Console.WriteLine($"SESSION: {session.SessionId}");
    Console.WriteLine("================================================================================");
    Console.WriteLine($"Title:        {session.Title}");
    Console.WriteLine($"Status:       {session.Status}");
    Console.WriteLine($"Created:      {session.CreatedAt:u}");
    Console.WriteLine($"LastActivity: {session.LastActivityAt:u}");
    Console.WriteLine($"Messages:     {messages.Count}");
    Console.WriteLine();

    foreach (var m in messages)
    {
        var roleHeader = m.Role.ToUpperInvariant();
        var color = m.Role == "user" ? ConsoleColor.Cyan : ConsoleColor.Green;

        Console.ForegroundColor = color;
        Console.WriteLine($"[{m.Timestamp:HH:mm:ss}] {roleHeader} ({m.Status}):");
        Console.ResetColor();
        Console.WriteLine(m.Content);

        if (m.Citations.Count > 0)
        {
            Console.WriteLine("  Citations: " + string.Join(", ", m.Citations.Select(c => $"[{c.Sequence}] {c.Title}")));
        }
        if (m.LinkedRunId != null)
        {
            Console.WriteLine($"  (LinkedRun: {m.LinkedRunId})");
        }
        Console.WriteLine();
    }

    return 0;
}

static async Task<int> HandleCreateSessionAsync(IServiceProvider sp, string[] args)
{
    string? title = null;
    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == "--title" && i + 1 < args.Length) title = args[++i];
    }

    var tenantContext = sp.GetRequiredService<ITenantContext>();
    var tenantId = tenantContext.GetTenantId();
    var sessionStore = sp.GetRequiredService<ISessionStore>();

    var session = await sessionStore.CreateSessionAsync(tenantId, title);

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"Session Created: {session.SessionId}");
    Console.ResetColor();
    Console.WriteLine($"Title:        {session.Title}");
    Console.WriteLine($"Status:       {session.Status}");
    Console.WriteLine($"CreatedAt:    {session.CreatedAt:u}");
    return 0;
}

static async Task<int> HandleListApprovalsAsync(IServiceProvider sp, string[] args)
{
    var tenantContext = sp.GetRequiredService<ITenantContext>();
    var tenantId = tenantContext.GetTenantId();
    var approvalManager = sp.GetRequiredService<IApprovalManager>();

    var list = await approvalManager.ListRequestsAsync(tenantId);

    Console.WriteLine($"Approval Requests ({list.Count}):");
    Console.WriteLine("--------------------------------------------------------------------------------------------------");
    Console.WriteLine($"{"RequestId",-36} {"Decision",-12} {"Executed",-10} {"Action",-25} {"CreatedAt (UTC)"}");
    Console.WriteLine("--------------------------------------------------------------------------------------------------");

    foreach (var a in list)
    {
        Console.WriteLine($"{a.RequestId,-36} {a.Decision,-12} {a.IsExecuted,-10} {a.ProposedAction,-25} {a.CreatedAt.UtcDateTime:yyyy-MM-dd HH:mm:ss}");
    }

    return 0;
}

static async Task<int> HandleGetApprovalAsync(IServiceProvider sp, string[] args)
{
    string? requestId = null;
    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == "--request-id" && i + 1 < args.Length) requestId = args[++i];
        else if (requestId == null && !args[i].StartsWith("--")) requestId = args[i];
    }

    if (string.IsNullOrWhiteSpace(requestId))
    {
        Console.WriteLine("Usage: approval <requestId>");
        return 1;
    }

    var tenantContext = sp.GetRequiredService<ITenantContext>();
    var tenantId = tenantContext.GetTenantId();
    var approvalManager = sp.GetRequiredService<IApprovalManager>();

    var apr = await approvalManager.GetRequestAsync(requestId, tenantId);
    if (apr == null)
    {
        Console.WriteLine($"Approval request '{requestId}' was not found for the authenticated tenant.");
        return 1;
    }

    Console.WriteLine("================================================================================");
    Console.WriteLine($"APPROVAL REQUEST: {apr.RequestId}");
    Console.WriteLine("================================================================================");
    Console.WriteLine($"Action:      {apr.ProposedAction}");
    Console.WriteLine($"Decision:    {apr.Decision}");
    Console.WriteLine($"Executed:    {apr.IsExecuted}");
    Console.WriteLine($"CreatedAt:   {apr.CreatedAt:u}");
    if (apr.DecidedAt != null) Console.WriteLine($"DecidedAt:   {apr.DecidedAt:u}");
    if (apr.ReviewerComments != null) Console.WriteLine($"Comments:    {apr.ReviewerComments}");
    Console.WriteLine();
    Console.WriteLine("Original Payload:");
    Console.WriteLine(apr.OriginalPayload);
    if (apr.EditedPayload != null)
    {
        Console.WriteLine();
        Console.WriteLine("Edited Payload:");
        Console.WriteLine(apr.EditedPayload);
    }

    return 0;
}

static async Task<int> HandleApproveAsync(IServiceProvider sp, string[] args)
{
    string? requestId = null;
    string? comments = null;

    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == "--comments" && i + 1 < args.Length) comments = args[++i];
        else if (requestId == null && !args[i].StartsWith("--")) requestId = args[i];
    }

    if (string.IsNullOrWhiteSpace(requestId))
    {
        Console.WriteLine("Usage: approve <requestId> [--comments <comments>]");
        return 1;
    }

    var tenantContext = sp.GetRequiredService<ITenantContext>();
    var tenantId = tenantContext.GetTenantId();
    var approvalManager = sp.GetRequiredService<IApprovalManager>();

    var apr = await approvalManager.SubmitDecisionAsync(requestId, tenantId, ApprovalDecision.Approved, comments);

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"Approval request '{requestId}' Approved.");
    Console.ResetColor();
    return 0;
}

static async Task<int> HandleRejectAsync(IServiceProvider sp, string[] args)
{
    string? requestId = null;
    string? comments = null;

    for (int i = 0; i < args.Length; i++)
    {
        if (args[i] == "--comments" && i + 1 < args.Length) comments = args[++i];
        else if (requestId == null && !args[i].StartsWith("--")) requestId = args[i];
    }

    if (string.IsNullOrWhiteSpace(requestId))
    {
        Console.WriteLine("Usage: reject <requestId> [--comments <comments>]");
        return 1;
    }

    var tenantContext = sp.GetRequiredService<ITenantContext>();
    var tenantId = tenantContext.GetTenantId();
    var approvalManager = sp.GetRequiredService<IApprovalManager>();

    var apr = await approvalManager.SubmitDecisionAsync(requestId, tenantId, ApprovalDecision.Rejected, comments);

    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"Approval request '{requestId}' Rejected.");
    Console.ResetColor();
    return 0;
}

static async Task<int> HandleExecuteApprovalAsync(IServiceProvider sp, string[] args)
{
    string? requestId = null;

    for (int i = 0; i < args.Length; i++)
    {
        if (requestId == null && !args[i].StartsWith("--")) requestId = args[i];
    }

    if (string.IsNullOrWhiteSpace(requestId))
    {
        Console.WriteLine("Usage: execute-approval <requestId>");
        return 1;
    }

    var tenantContext = sp.GetRequiredService<ITenantContext>();
    var tenantId = tenantContext.GetTenantId();
    var approvalManager = sp.GetRequiredService<IApprovalManager>();

    var result = await approvalManager.ExecuteActionAsync(requestId, tenantId);

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"Action Executed: {result.RequestId}");
    Console.ResetColor();
    Console.WriteLine($"Status:  {result.Status}");
    Console.WriteLine($"Message: {result.Message}");
    return 0;
}

static void PrintHelp()
{
    Console.WriteLine("================================================================================");
    Console.WriteLine(" Government Domain Copilot — Client CLI (FR-7 / FR-8)");
    Console.WriteLine("================================================================================");
    Console.WriteLine();
    Console.WriteLine("Usage: dotnet run --project src/ClientCli -- <command> [arguments]");
    Console.WriteLine();
    Console.WriteLine("Global Options:");
    Console.WriteLine("  --api-key <key>    Authenticate with a synthetic test API key (FR-8).");
    Console.WriteLine("                     Required for production use; optional in dev mode.");
    Console.WriteLine("                     Never log or commit real API keys.");
    Console.WriteLine();
    Console.WriteLine("Available Commands:");
    Console.WriteLine();
    Console.WriteLine("  ingest             Ingest a government document into the retrieval store");
    Console.WriteLine("                     --title <title> --ref <sourceReference> (--file <path> | --text <text>)");
    Console.WriteLine();
    Console.WriteLine("  seed-corpus        Ingest manifest-assigned synthetic corpus documents for the authenticated tenant");
    Console.WriteLine("  validate-corpus    Validate committed corpus count, explicit pages, metadata and tenant distribution");
    Console.WriteLine();
    Console.WriteLine("  ask                Generate an evidence-grounded answer or refusal for a query");
    Console.WriteLine("                     \"<query>\" [--top-k <k>] [--session-id <id>]");
    Console.WriteLine();
    Console.WriteLine("  run                Execute the multi-agent orchestration pipeline for a query");
    Console.WriteLine("                     \"<query>\" [--correlation-id <id>] [--session-id <id>]");
    Console.WriteLine();
    Console.WriteLine("  runs               List recent orchestration run traces for the current tenant");
    Console.WriteLine("                     [--limit <n>] [--session-id <id>]");
    Console.WriteLine();
    Console.WriteLine("  trace              Inspect detailed telemetry and tool calls for a run trace");
    Console.WriteLine("                     <runId>");
    Console.WriteLine();
    Console.WriteLine("  sessions           List conversation sessions for the current tenant");
    Console.WriteLine("                     [--limit <n>]");
    Console.WriteLine();
    Console.WriteLine("  session            View conversation history and citations for a session");
    Console.WriteLine("                     <sessionId>");
    Console.WriteLine();
    Console.WriteLine("  session-create     Create a new server-side conversation session");
    Console.WriteLine("                     [--title <title>]");
    Console.WriteLine();
    Console.WriteLine("  approvals          List all approval requests for the current tenant");
    Console.WriteLine();
    Console.WriteLine("  approval           Inspect details of a specific approval request");
    Console.WriteLine("                     <requestId>");
    Console.WriteLine();
    Console.WriteLine("  approve            Approve a pending human-in-the-loop approval request");
    Console.WriteLine("                     <requestId> [--comments <comments>]");
    Console.WriteLine();
    Console.WriteLine("  reject             Reject a pending human-in-the-loop approval request");
    Console.WriteLine("                     <requestId> [--comments <comments>]");
    Console.WriteLine();
    Console.WriteLine("  execute-approval   Safely stage and execute an approved action");
    Console.WriteLine("                     <requestId>");
    Console.WriteLine();
    Console.WriteLine("  --help, -h         Display this help message");
    Console.WriteLine();
    Console.WriteLine("Tenant Security Note:");
    Console.WriteLine("  Tenant context is enforced server-side through ITenantContext configuration.");
    Console.WriteLine("  Client commands cannot arbitrarily spoof or override tenant identity.");
    Console.WriteLine();
}

internal sealed class CliUserContext(GovernmentDomainCopilot.Infrastructure.Auth.TestUserRecord? user) : ICurrentUserContext
{
    public bool IsAuthenticated => user != null;
    public Guid? UserId => user?.UserId;
    public string? ExternalId => user?.ExternalId;
    public string? DisplayName => user?.DisplayName;
    public Guid? TenantId => user?.TenantId;
    public IReadOnlyList<string> Roles => user != null ? new[] { user.Role } : Array.Empty<string>();
    public bool IsInRole(string role) => user != null && string.Equals(user.Role, role, StringComparison.OrdinalIgnoreCase);
}

internal sealed class CliTenantContext(Guid tenantId) : ITenantContext
{
    public Guid GetTenantId() => tenantId;
}

internal sealed class CliHostEnvironment(string environmentName) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = environmentName;
    public string ApplicationName { get; set; } = "GovernmentDomainCopilot.ClientCli";
    public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
