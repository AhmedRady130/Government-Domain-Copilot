using GovernmentDomainCopilot.Application.Documents.Commands;

namespace GovernmentDomainCopilot.Application.Documents.Validation;

/// <summary>
/// Validates an <see cref="IngestDocumentCommand"/> against all application-layer
/// ingestion constraints before the command reaches the use-case handler.
/// </summary>
/// <remarks>
/// This is a pure static validator — it has no external dependencies and requires
/// no mocking in unit tests. An empty result list indicates a valid command.
///
/// All size thresholds are defined in <see cref="IngestionLimits"/> so that
/// changing a limit in one place automatically propagates to both this validator
/// and its tests.
/// </remarks>
public static class IngestDocumentCommandValidator
{
    /// <summary>
    /// Validates <paramref name="command"/> and returns all constraint violations found.
    /// </summary>
    /// <param name="command">The command to validate. Must not be <see langword="null"/>.</param>
    /// <returns>
    /// A read-only list of <see cref="ValidationError"/> instances.
    /// An empty list means the command is valid and may be passed to the use-case handler.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="command"/> is <see langword="null"/>.
    /// </exception>
    public static IReadOnlyList<ValidationError> Validate(IngestDocumentCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = new List<ValidationError>();

        ValidateTitle(command.Title, errors);
        ValidateSourceReference(command.SourceReference, errors);
        ValidateSourceText(command.SourceText, errors);

        return errors;
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static void ValidateTitle(string title, List<ValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            errors.Add(new ValidationError(
                nameof(IngestDocumentCommand.Title),
                "Title must not be empty or whitespace."));
            return; // length check is meaningless on an empty/whitespace value
        }

        if (title.Length > IngestionLimits.MaxTitleLength)
        {
            errors.Add(new ValidationError(
                nameof(IngestDocumentCommand.Title),
                $"Title must not exceed {IngestionLimits.MaxTitleLength} characters " +
                $"(received {title.Length})."));
        }
    }

    private static void ValidateSourceReference(string sourceReference, List<ValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(sourceReference))
        {
            errors.Add(new ValidationError(
                nameof(IngestDocumentCommand.SourceReference),
                "Source reference must not be empty or whitespace."));
            return;
        }

        if (sourceReference.Length > IngestionLimits.MaxSourceReferenceLength)
        {
            errors.Add(new ValidationError(
                nameof(IngestDocumentCommand.SourceReference),
                $"Source reference must not exceed {IngestionLimits.MaxSourceReferenceLength} characters " +
                $"(received {sourceReference.Length})."));
        }
    }

    private static void ValidateSourceText(string sourceText, List<ValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            errors.Add(new ValidationError(
                nameof(IngestDocumentCommand.SourceText),
                "Source text must not be empty or whitespace."));
            return;
        }

        if (sourceText.Length > IngestionLimits.MaxSourceTextLength)
        {
            errors.Add(new ValidationError(
                nameof(IngestDocumentCommand.SourceText),
                $"Source text must not exceed {IngestionLimits.MaxSourceTextLength} characters " +
                $"(received {sourceText.Length})."));
        }
    }
}
