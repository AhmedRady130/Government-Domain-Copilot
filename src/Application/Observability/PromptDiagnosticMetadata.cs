namespace GovernmentDomainCopilot.Application.Observability;

/// <summary>
/// Safe diagnostic metadata for user-controlled prompts. It intentionally retains
/// only presence and length, never prompt text, PII, credentials, or evidence.
/// </summary>
public readonly record struct PromptDiagnosticMetadata(bool IsPresent, int Length)
{
    public static PromptDiagnosticMetadata From(string? prompt) =>
        new(!string.IsNullOrWhiteSpace(prompt), prompt?.Length ?? 0);
}
