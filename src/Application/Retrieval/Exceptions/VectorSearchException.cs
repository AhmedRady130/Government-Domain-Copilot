namespace GovernmentDomainCopilot.Application.Retrieval.Exceptions;

public sealed class VectorSearchException : Exception
{
    public VectorSearchException(string message) : base(message)
    {
    }

    public VectorSearchException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
