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
}
