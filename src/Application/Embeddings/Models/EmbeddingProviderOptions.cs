namespace GovernmentDomainCopilot.Application.Embeddings.Models;

/// <summary>
/// Configuration options for embedding provider selection, models, dimensions, and limits.
/// </summary>
public sealed class EmbeddingProviderOptions
{
    public const string SectionName = "EmbeddingProviders";

    /// <summary>
    /// Primary provider name ("Gemini", "Ollama", etc.). Default is "Gemini".
    /// </summary>
    public string PrimaryProvider { get; set; } = "Gemini";

    /// <summary>
    /// Optional fallback provider name ("Ollama", etc.).
    /// </summary>
    public string? FallbackProvider { get; set; } = "Ollama";

    /// <summary>
    /// Primary model identifier. Default is "gemini-embedding-2".
    /// </summary>
    public string PrimaryModel { get; set; } = "gemini-embedding-2";

    /// <summary>
    /// Fallback model identifier. Default is "nomic-embed-text".
    /// </summary>
    public string FallbackModel { get; set; } = "nomic-embed-text";

    /// <summary>
    /// Expected vector dimension size. Default is 768.
    /// </summary>
    public int ExpectedDimensions { get; set; } = 768;

    /// <summary>
    /// Maximum batch size for a single embedding call. Default is 100.
    /// </summary>
    public int MaxBatchSize { get; set; } = 100;

    /// <summary>
    /// HTTP endpoint URL for Gemini API (e.g. "https://generativelanguage.googleapis.com").
    /// </summary>
    public string GeminiBaseUrl { get; set; } = "https://generativelanguage.googleapis.com";

    /// <summary>
    /// HTTP endpoint URL for local Ollama provider (e.g. "http://localhost:11434").
    /// </summary>
    public string OllamaBaseUrl { get; set; } = "http://localhost:11434";
}
