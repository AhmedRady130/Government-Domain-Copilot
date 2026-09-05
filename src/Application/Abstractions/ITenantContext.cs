namespace GovernmentDomainCopilot.Application.Abstractions;

/// <summary>
/// Provides the authenticated tenant identity for the current execution context.
/// </summary>
/// <remarks>
/// The tenant identifier is always sourced from the server-side authenticated principal
/// (e.g. a validated JWT claim). It is never accepted from a client-supplied request
/// body, query string, or header, in compliance with AGENTS.md multi-tenancy rules.
///
/// Implementations live in Infrastructure (e.g. reading from IHttpContextAccessor).
/// In unit tests, implement this interface inline or with a simple stub — no mocking
/// library is required.
/// </remarks>
public interface ITenantContext
{
    /// <summary>
    /// Returns the current tenant's unique identifier.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no authenticated tenant context is available in the current execution scope.
    /// </exception>
    Guid GetTenantId();
}
