namespace GovernmentDomainCopilot.Application.Embeddings.Exceptions;

/// <summary>
/// Base exception type for all application embedding failures.
/// </summary>
public class EmbeddingException : Exception
{
    public EmbeddingException(string message) : base(message)
    {
    }

    public EmbeddingException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Thrown when embedding request payload or inputs are invalid.
/// </summary>
public sealed class EmbeddingInvalidInputException : EmbeddingException
{
    public EmbeddingInvalidInputException(string message) : base(message)
    {
    }
}

/// <summary>
/// Thrown when an embedding provider is unreachable, unavailable, or encounters network/transient failures.
/// </summary>
public sealed class EmbeddingProviderUnavailableException : EmbeddingException
{
    public string ProviderName { get; }

    public EmbeddingProviderUnavailableException(string providerName, string message)
        : base($"Embedding provider '{providerName}' unavailable: {message}")
    {
        ProviderName = providerName;
    }

    public EmbeddingProviderUnavailableException(string providerName, string message, Exception innerException)
        : base($"Embedding provider '{providerName}' unavailable: {message}", innerException)
    {
        ProviderName = providerName;
    }
}

/// <summary>
/// Thrown when embedding provider configuration or settings are invalid.
/// </summary>
public sealed class EmbeddingConfigurationException : EmbeddingException
{
    public EmbeddingConfigurationException(string message) : base(message)
    {
    }
}

/// <summary>
/// Thrown when a returned vector dimension size does not match the configured expected size.
/// </summary>
public sealed class EmbeddingDimensionMismatchException : EmbeddingException
{
    public string ProviderName { get; }
    public int ExpectedDimension { get; }
    public int ActualDimension { get; }

    public EmbeddingDimensionMismatchException(string providerName, int expectedDimension, int actualDimension)
        : base($"Embedding vector dimension mismatch for provider '{providerName}'. Expected {expectedDimension}, received {actualDimension}.")
    {
        ProviderName = providerName;
        ExpectedDimension = expectedDimension;
        ActualDimension = actualDimension;
    }
}

/// <summary>
/// Thrown when an embedding provider returns a rate limit or HTTP 429 response.
/// </summary>
public sealed class EmbeddingRateLimitException : EmbeddingException
{
    public string ProviderName { get; }

    public EmbeddingRateLimitException(string providerName, string message)
        : base($"Embedding provider '{providerName}' rate limit exceeded: {message}")
    {
        ProviderName = providerName;
    }
}
