namespace GovernmentDomainCopilot.Application.Documents.Validation;

/// <summary>
/// Defines the validated size and length boundaries for the document ingestion pipeline.
/// </summary>
/// <remarks>
/// These constants enforce both usability and security constraints:
/// <list type="bullet">
///   <item>
///     <see cref="MaxTitleLength"/> and <see cref="MaxSourceReferenceLength"/> mirror the
///     database schema column lengths defined in Infrastructure to fail fast at the
///     application boundary, before any persistence is attempted.
///   </item>
///   <item>
///     <see cref="MaxSourceTextLength"/> bounds memory consumption during normalisation and
///     chunking, defending against denial-of-service via arbitrarily large payloads.
///   </item>
/// </list>
/// Update these values in a single place here; all validators and tests reference
/// these constants automatically.
/// </remarks>
public static class IngestionLimits
{
    /// <summary>
    /// Maximum number of characters allowed in a document title.
    /// Matches the <c>character varying(500)</c> column constraint on the Documents table.
    /// </summary>
    public const int MaxTitleLength = 500;

    /// <summary>
    /// Maximum number of characters allowed in a source reference.
    /// Matches the <c>character varying(2000)</c> column constraint on the Documents table.
    /// </summary>
    public const int MaxSourceReferenceLength = 2_000;

    /// <summary>
    /// Maximum number of characters accepted for the full source text of a document.
    /// This application-layer limit prevents unbounded memory usage during normalisation
    /// and chunking and bounds the maximum number of persisted chunks per document.
    /// Approximately 500 KB of UTF-16 text.
    /// </summary>
    public const int MaxSourceTextLength = 500_000;
}
