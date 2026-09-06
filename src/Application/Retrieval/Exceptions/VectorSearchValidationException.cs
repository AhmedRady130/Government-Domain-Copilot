namespace GovernmentDomainCopilot.Application.Retrieval.Exceptions;

public sealed class VectorSearchValidationException : Exception
{
    public VectorSearchValidationException(string message) : base(message)
    {
    }

    public VectorSearchValidationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
