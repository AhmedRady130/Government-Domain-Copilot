namespace GovernmentDomainCopilot.Application.Documents.Validation;

/// <summary>
/// Represents a single validation failure produced by
/// <see cref="IngestDocumentCommandValidator"/>.
/// </summary>
/// <param name="PropertyName">
/// The name of the command property that failed validation,
/// using the same casing as the property declaration (e.g. <c>nameof(IngestDocumentCommand.Title)</c>).
/// </param>
/// <param name="Message">
/// A human-readable description of the constraint that was violated.
/// Suitable for returning to an API caller as a validation error detail.
/// </param>
public sealed record ValidationError(string PropertyName, string Message);
