namespace GovernmentDomainCopilot.Application.Embeddings.Models;

/// <summary>
/// Represents a typed request to generate embeddings for one or more text inputs.
/// </summary>
public sealed record EmbeddingRequest
{
    public EmbeddingRequest(
        IReadOnlyList<string> inputs,
        string? model = null,
        Guid? tenantId = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        if (inputs.Count == 0)
        {
            throw new ArgumentException("Embedding request must contain at least one text input.", nameof(inputs));
        }

        if (inputs.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Embedding request inputs cannot contain null or empty strings.", nameof(inputs));
        }

        Inputs = inputs;
        Model = model;
        TenantId = tenantId;
    }

    /// <summary>
    /// The ordered list of text inputs to embed.
    /// </summary>
    public IReadOnlyList<string> Inputs { get; }

    /// <summary>
    /// Optional model override identifier.
    /// </summary>
    public string? Model { get; }

    /// <summary>
    /// Optional tenant identifier for correlation and policy tracing (never embedded into vector values).
    /// </summary>
    public Guid? TenantId { get; }
}
