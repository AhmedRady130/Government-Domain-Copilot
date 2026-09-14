namespace GovernmentDomainCopilot.Application.Abstractions;

/// <summary>
/// Scoped correlation context interface that propagates correlation IDs across
/// HTTP requests, orchestrators, agent executions, and LLM calls.
/// Independent of ASP.NET Core HttpContext.
/// </summary>
public interface ICorrelationContext
{
    /// <summary>
    /// Gets the active correlation ID.
    /// </summary>
    string CorrelationId { get; }

    /// <summary>
    /// Sets or overrides the active correlation ID in the current execution scope.
    /// </summary>
    void SetCorrelationId(string correlationId);
}
