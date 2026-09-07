namespace GovernmentDomainCopilot.Infrastructure.LLM.Providers;

public sealed class LlmProviderOptions
{
    public const string SectionName = "LlmProviders";

    public string GeminiBaseUrl { get; set; } = "https://generativelanguage.googleapis.com";

    public string PrimaryModel { get; set; } = "gemini-2.5-flash";

    public int DefaultMaxOutputTokens { get; set; } = 1024;

    public double DefaultTemperature { get; set; } = 0.1;

    public int HttpTimeoutSeconds { get; set; } = 30;
}
