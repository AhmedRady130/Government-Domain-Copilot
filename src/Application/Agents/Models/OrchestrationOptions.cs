namespace GovernmentDomainCopilot.Application.Agents.Models;

public sealed class OrchestrationOptions
{
    public const string SectionName = "Orchestration";

    public int MaxIterations { get; set; } = 5;
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxRetries { get; set; } = 2;
    public int RetryBackoffMilliseconds { get; set; } = 50;
    public bool EnableFallback { get; set; } = true;
}
