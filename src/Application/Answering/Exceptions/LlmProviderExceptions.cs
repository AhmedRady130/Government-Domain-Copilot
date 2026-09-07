namespace GovernmentDomainCopilot.Application.Answering.Exceptions;

public class LlmProviderException : Exception
{
    public string ProviderName { get; }

    public LlmProviderException(string providerName, string message) : base(message)
    {
        ProviderName = providerName;
    }

    public LlmProviderException(string providerName, string message, Exception innerException) : base(message, innerException)
    {
        ProviderName = providerName;
    }
}

public sealed class LlmProviderUnavailableException : LlmProviderException
{
    public LlmProviderUnavailableException(string providerName, string message)
        : base(providerName, message) { }

    public LlmProviderUnavailableException(string providerName, string message, Exception innerException)
        : base(providerName, message, innerException) { }
}

public sealed class LlmRateLimitException : LlmProviderException
{
    public LlmRateLimitException(string providerName, string message)
        : base(providerName, message) { }
}
