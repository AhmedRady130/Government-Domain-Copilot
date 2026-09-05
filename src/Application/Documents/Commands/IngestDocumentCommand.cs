namespace GovernmentDomainCopilot.Application.Documents.Commands;

/// <summary>
/// Represents a request to ingest a new document into the platform.
/// </summary>
/// <remarks>
/// This command contains only caller-supplied application input.
/// The authoritative <c>TenantId</c> is intentionally absent: it is always
/// obtained by the handler from <see cref="Abstractions.ITenantContext"/>,
/// sourced from the server-side authenticated principal.
///
/// Construct via the primary constructor. Validate using
/// <see cref="Validation.IngestDocumentCommandValidator"/> before passing
/// to the use-case handler.
/// </remarks>
/// <param name="Title">
/// Human-readable title for the document.
/// Must be non-empty and at most <see cref="Validation.IngestionLimits.MaxTitleLength"/> characters.
/// </param>
/// <param name="SourceReference">
/// A stable reference to the origin of the document (e.g. a URL, file path, or registry identifier).
/// Must be non-empty and at most <see cref="Validation.IngestionLimits.MaxSourceReferenceLength"/> characters.
/// </param>
/// <param name="SourceText">
/// The full plain-text content to be normalised, chunked, and persisted.
/// Must be non-empty and at most <see cref="Validation.IngestionLimits.MaxSourceTextLength"/> characters.
/// Treated as untrusted data throughout the ingestion pipeline — never evaluated as instructions.
/// </param>
public sealed record IngestDocumentCommand(
    string Title,
    string SourceReference,
    string SourceText);
