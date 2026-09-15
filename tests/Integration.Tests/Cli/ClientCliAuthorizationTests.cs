using System.Diagnostics;
using System.IO;
using Xunit;

namespace Integration.Tests.Cli;

public sealed class ClientCliAuthorizationTests
{
    private static (int ExitCode, string StdOut, string StdErr) RunCli(params string[] args)
    {
        var cliDll = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "ClientCli", "bin", "Release", "net9.0", "GovernmentDomainCopilot.ClientCli.dll"));

        if (!File.Exists(cliDll))
        {
            cliDll = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                "src", "ClientCli", "bin", "Debug", "net9.0", "GovernmentDomainCopilot.ClientCli.dll"));
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.Environment.Remove("ConnectionStrings__GovernmentDomainCopilot");
        startInfo.ArgumentList.Add(cliDll);
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(10000);

        return (process.ExitCode, stdout, stderr);
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    [InlineData("execute-approval")]
    public void AnonymousApprovalActions_AreDenied(string command)
    {
        var (exitCode, stdout, stderr) = RunCli(command, "req-test-1");
        Assert.NotEqual(0, exitCode);
        Assert.Contains($"Access Denied: The '{command}' command requires an authenticated 'Supervisor'.", stderr);
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    [InlineData("execute-approval")]
    public void OfficerApprovalActions_AreDenied(string command)
    {
        var (exitCode, stdout, stderr) = RunCli(command, "req-test-1", "--api-key", "gov-key-tenant-a-officer");
        Assert.NotEqual(0, exitCode);
        Assert.Contains($"Access Denied: The '{command}' command requires an authenticated 'Supervisor'.", stderr);
    }

    [Fact]
    public void SupervisorApprovalAction_PassesAuthorizationGate()
    {
        var (exitCode, stdout, stderr) = RunCli("approve", "req-test-1", "--api-key", "gov-key-tenant-a-supervisor");
        Assert.DoesNotContain("Access Denied", stderr);
        Assert.DoesNotContain("Access Denied", stdout);
        Assert.Contains("Tenant A Supervisor", stdout);
    }

    [Fact]
    public void ArbitraryTenantId_IsRejected()
    {
        var (exitCode, stdout, stderr) = RunCli("--tenant-id", "11111111-1111-1111-1111-111111111111");
        Assert.NotEqual(0, exitCode);
        Assert.Contains("Arbitrary --tenant-id parameter is forbidden", stderr);
    }

    [Fact]
    public void ValidateCorpus_WorksWithoutADatabaseConnection()
    {
        var (exitCode, stdout, stderr) = RunCli("validate-corpus");

        Assert.Equal(0, exitCode);
        Assert.Empty(stderr);
        Assert.Contains("Documents: 32", stdout);
        Assert.Contains("Pages: 160", stdout);
        Assert.Contains("Corpus validation: PASS", stdout);
    }

    [Fact]
    public void SeedCorpus_WithoutApiKey_IsRejectedWithoutSelectingTheDevelopmentTenant()
    {
        var (exitCode, stdout, stderr) = RunCli("seed-corpus");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("requires an authenticated API-key identity", stderr);
        Assert.DoesNotContain("Tenant A", stdout);
        Assert.DoesNotContain("Tenant A", stderr);
    }

    [Theory]
    [InlineData("gov-key-tenant-a-officer", "11111111-1111-1111-1111-111111111111", "synthetic-gov-01", "synthetic-gov-02")]
    [InlineData("gov-key-tenant-b-officer", "22222222-2222-2222-2222-222222222222", "synthetic-gov-02", "synthetic-gov-01")]
    public void SeedCorpus_WithAuthenticatedIdentity_SeedsOnlyThatTenantsManifestDocuments(
        string apiKey,
        string tenantId,
        string expectedDocument,
        string otherTenantDocument)
    {
        var (exitCode, stdout, stderr) = RunCli("seed-corpus", "--api-key", apiKey);

        Assert.Equal(0, exitCode);
        Assert.Empty(stderr);
        Assert.Contains($"Seed complete: 16 succeeded, 0 failed for authenticated tenant {tenantId}.", stdout);
        Assert.Contains(expectedDocument, stdout);
        Assert.DoesNotContain(otherTenantDocument, stdout);
    }
}
