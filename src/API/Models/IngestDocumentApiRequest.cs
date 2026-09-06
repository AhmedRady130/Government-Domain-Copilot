namespace GovernmentDomainCopilot.API.Models;

/// <summary>
/// API request payload for document ingestion.
/// </summary>
/// <remarks>
/// Contains caller-supplied document title, source reference, and text content.
/// Multi-tenancy rule: <c>TenantId</c> is intentionally omitted — authoritative tenant identity
/// is always resolved server-side.
/// </remarks>
/// <param name="Title">Human-readable document title.</param>
/// <param name="SourceReference">Stable source reference identifier (URL, URI, or registry ID).</param>
/// <param name="SourceText">Plain-text content to be normalised, chunked, and persisted.</param>
public sealed record IngestDocumentApiRequest(
    string Title,
    string SourceReference,
    string SourceText);
