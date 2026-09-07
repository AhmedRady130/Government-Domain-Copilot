// Government Domain Copilot — Evaluation Harness CLI
// Runs golden dataset evaluation cases against the grounded answer pipeline
// and produces deterministic metrics report.

using System.Text.Json;
using GovernmentDomainCopilot.Application;
using GovernmentDomainCopilot.Application.Abstractions;
using GovernmentDomainCopilot.Application.Evaluation.Abstractions;
using GovernmentDomainCopilot.Application.Evaluation.Models;
using GovernmentDomainCopilot.Infrastructure;
using GovernmentDomainCopilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

Console.WriteLine("=======================================================");
Console.WriteLine(" Government Domain Copilot — Evaluation Harness v1.0  ");
Console.WriteLine("=======================================================");
Console.WriteLine();

// --- Parse CLI arguments ---
string? datasetPath = null;
string? outputPath = null;

for (int i = 0; i < args.Length; i++)
{
    if ((args[i] == "--dataset" || args[i] == "-d") && i + 1 < args.Length)
        datasetPath = args[++i];
    else if ((args[i] == "--output" || args[i] == "-o") && i + 1 < args.Length)
        outputPath = args[++i];
    else if (args[i] == "--help" || args[i] == "-h")
    {
        PrintHelp();
        return 0;
    }
}

// --- Build configuration ---
// Provide a dummy connection string so AddInfrastructure doesn't throw.
// The DbContext is overridden with InMemory below.
var inMemoryConfig = new Dictionary<string, string?>
{
    ["ConnectionStrings:GovernmentDomainCopilot"] = "Host=localhost;Database=eval_dummy",
};

var configuration = new ConfigurationBuilder()
    .AddInMemoryCollection(inMemoryConfig)
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

// --- Build DI container ---
var services = new ServiceCollection();
services.AddLogging(b =>
{
    b.AddConsole();
    b.SetMinimumLevel(LogLevel.Warning);
});

services.AddApplication();
services.AddInfrastructure(configuration);

// Override DbContext with InMemory (no real PostgreSQL required for harness)
services.AddDbContext<GovernmentDomainCopilotDbContext>(opts =>
{
    opts.UseInMemoryDatabase("EvaluationHarnessDb");
    opts.ConfigureWarnings(w =>
        w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning));
}, ServiceLifetime.Scoped, ServiceLifetime.Scoped);

// Override ITenantContext with EvaluationTenantContext (enables per-case tenant switching)
var existingTenantContext = services.FirstOrDefault(d => d.ServiceType == typeof(ITenantContext));
if (existingTenantContext != null) services.Remove(existingTenantContext);
services.AddSingleton<ITenantContext>(sp => sp.GetRequiredService<IEvaluationTenantContext>());

await using var serviceProvider = services.BuildServiceProvider();

// --- Load dataset ---
var loader = serviceProvider.GetRequiredService<IGoldenDatasetLoader>();
IReadOnlyList<EvaluationCase> cases;

try
{
    if (!string.IsNullOrWhiteSpace(datasetPath))
    {
        Console.WriteLine($"Loading dataset from: {datasetPath}");
        cases = loader.LoadFromFile(datasetPath);
    }
    else
    {
        Console.WriteLine("Loading default embedded golden dataset...");
        cases = loader.LoadDefault();
    }

    Console.WriteLine($"Loaded {cases.Count} evaluation cases ({cases.Count(c => c.IsAdversarial)} adversarial).");
    Console.WriteLine();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Failed to load evaluation dataset: {ex.Message}");
    return 2;
}

// --- Run harness ---
using var scope = serviceProvider.CreateScope();
var harness = scope.ServiceProvider.GetRequiredService<IEvaluationHarness>();

Console.WriteLine("Running evaluation harness...");
Console.WriteLine();

EvaluationReport report;
try
{
    report = await harness.RunAsync(cases, CancellationToken.None);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Evaluation harness encountered an unexpected error: {ex.Message}");
    return 3;
}

// --- Print human-readable summary ---
PrintSummary(report);

// --- Write JSON report ---
if (!string.IsNullOrWhiteSpace(outputPath))
{
    try
    {
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(outputPath, json);
        Console.WriteLine();
        Console.WriteLine($"Machine-readable JSON report written to: {outputPath}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Warning: Failed to write JSON report to '{outputPath}': {ex.Message}");
    }
}

Console.WriteLine();
return report.FailedCases > 0 ? 1 : 0;

// ─── Local functions ────────────────────────────────────────────────────────

static void PrintSummary(EvaluationReport report)
{
    Console.WriteLine("╔══════════════════════════════════════════════════════╗");
    Console.WriteLine("║              EVALUATION SUMMARY REPORT               ║");
    Console.WriteLine("╠══════════════════════════════════════════════════════╣");
    Console.WriteLine($"║  Timestamp       : {report.Timestamp:yyyy-MM-dd HH:mm:ss UTC}         ║");
    Console.WriteLine($"║  Total Cases     : {report.TotalCases,-5}                                ║");
    Console.WriteLine($"║  Passed          : {report.PassedCases,-5}                                ║");
    Console.WriteLine($"║  Failed          : {report.FailedCases,-5}                                ║");
    Console.WriteLine($"║  Duration        : {report.ExecutionDuration.TotalMilliseconds:F0} ms                              ║");
    Console.WriteLine("╠══════════════════════════════════════════════════════╣");
    Console.WriteLine($"║  Retrieval Hit Rate   : {report.RetrievalHitRate * 100,6:F1}%                      ║");
    Console.WriteLine($"║  Groundedness Score   : {report.GroundednessScore * 100,6:F1}%                      ║");
    Console.WriteLine($"║  Refusal Correctness  : {report.RefusalCorrectness * 100,6:F1}%                      ║");
    Console.WriteLine("╠══════════════════════════════════════════════════════╣");

    if (report.FailedCases > 0)
    {
        Console.WriteLine("║  FAILED CASES:                                       ║");
        foreach (var r in report.CaseResults.Where(r => !r.Passed))
        {
            var id = r.CaseId.Length > 20 ? r.CaseId[..20] : r.CaseId.PadRight(20);
            Console.WriteLine($"║  ✗ {id} | {r.ActualStatus,-8} | {(r.FailureReason ?? "Unknown")[..Math.Min((r.FailureReason ?? "Unknown").Length, 16)],-16} ║");
        }
    }
    else
    {
        Console.WriteLine("║  All cases PASSED ✓                                  ║");
    }

    Console.WriteLine("╚══════════════════════════════════════════════════════╝");
}

static void PrintHelp()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run --project src/EvaluationRunner [options]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --dataset, -d <path>   Path to custom golden dataset JSON file.");
    Console.WriteLine("                         Default: embedded golden-dataset-v1.json");
    Console.WriteLine("  --output, -o  <path>   Path to write machine-readable JSON report.");
    Console.WriteLine("  --help, -h             Show this help message.");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  dotnet run --project src/EvaluationRunner");
    Console.WriteLine("  dotnet run --project src/EvaluationRunner -- --output report.json");
    Console.WriteLine("  dotnet run --project src/EvaluationRunner -- --dataset data/custom-dataset.json --output report.json");
    Console.WriteLine();
    Console.WriteLine("Exit codes:");
    Console.WriteLine("  0 = All cases passed");
    Console.WriteLine("  1 = One or more cases failed");
    Console.WriteLine("  2 = Dataset load error");
    Console.WriteLine("  3 = Harness execution error");
}
