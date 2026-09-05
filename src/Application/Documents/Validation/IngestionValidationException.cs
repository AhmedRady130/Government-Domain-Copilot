namespace GovernmentDomainCopilot.Application.Documents.Validation;

/// <summary>
/// Thrown by the ingestion use-case handler when an
/// <see cref="Commands.IngestDocumentCommand"/> fails one or more validation
/// constraints defined in <see cref="IngestDocumentCommandValidator"/>.
/// </summary>
/// <remarks>
/// The caller (e.g. an API endpoint) should catch this exception and translate
/// it into an appropriate HTTP 400 response, including the
/// <see cref="Errors"/> collection as structured validation details.
/// </remarks>
public sealed class IngestionValidationException : Exception
{
    /// <summary>
    /// Initialises a new <see cref="IngestionValidationException"/> with the
    /// full list of constraint violations.
    /// </summary>
    /// <param name="errors">
    /// One or more <see cref="ValidationError"/> values. Must not be null or empty.
    /// </param>
    public IngestionValidationException(IReadOnlyList<ValidationError> errors)
        : base(BuildMessage(errors))
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (errors.Count == 0)
            throw new ArgumentException("At least one validation error is required.", nameof(errors));

        Errors = errors;
    }

    /// <summary>
    /// The complete list of constraint violations that caused this exception.
    /// </summary>
    public IReadOnlyList<ValidationError> Errors { get; }

    private static string BuildMessage(IReadOnlyList<ValidationError> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        return $"Document ingestion validation failed with {errors.Count} error(s): " +
               string.Join("; ", errors.Select(e => $"[{e.PropertyName}] {e.Message}"));
    }
}
