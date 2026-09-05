using GovernmentDomainCopilot.Application.Documents.Commands;
using GovernmentDomainCopilot.Application.Documents.Validation;

namespace Application.Tests.Documents.Validation;

/// <summary>
/// Unit tests for <see cref="IngestDocumentCommandValidator"/>.
/// No external dependencies or mocking library required — the validator is a
/// pure static function.
/// </summary>
public sealed class IngestDocumentCommandValidatorTests
{
    // =========================================================================
    // Helpers
    // =========================================================================

    /// <summary>Builds a fully valid command that passes all constraints.</summary>
    private static IngestDocumentCommand ValidCommand(
        string? title = null,
        string? sourceReference = null,
        string? sourceText = null)
        => new(
            Title:           title           ?? "Valid Document Title",
            SourceReference: sourceReference ?? "https://gov.example/doc/1",
            SourceText:      sourceText      ?? "Valid source text for ingestion.");

    // =========================================================================
    // Happy path
    // =========================================================================

    [Fact]
    public void Valid_command_produces_no_errors()
    {
        var errors = IngestDocumentCommandValidator.Validate(ValidCommand());

        Assert.Empty(errors);
    }

    // =========================================================================
    // Null command guard
    // =========================================================================

    [Fact]
    public void Null_command_throws_ArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            IngestDocumentCommandValidator.Validate(null!));
    }

    // =========================================================================
    // Title validation
    // =========================================================================

    [Fact]
    public void Empty_title_produces_title_error()
    {
        var errors = IngestDocumentCommandValidator.Validate(ValidCommand(title: ""));

        var error = Assert.Single(errors);
        Assert.Equal(nameof(IngestDocumentCommand.Title), error.PropertyName);
    }

    [Fact]
    public void Whitespace_only_title_produces_title_error()
    {
        var errors = IngestDocumentCommandValidator.Validate(ValidCommand(title: "   "));

        var error = Assert.Single(errors);
        Assert.Equal(nameof(IngestDocumentCommand.Title), error.PropertyName);
    }

    [Fact]
    public void Title_at_exact_max_length_is_valid()
    {
        var title = new string('A', IngestionLimits.MaxTitleLength);

        var errors = IngestDocumentCommandValidator.Validate(ValidCommand(title: title));

        Assert.Empty(errors);
    }

    [Fact]
    public void Title_one_character_over_max_length_produces_title_error()
    {
        var title = new string('A', IngestionLimits.MaxTitleLength + 1);

        var errors = IngestDocumentCommandValidator.Validate(ValidCommand(title: title));

        var error = Assert.Single(errors);
        Assert.Equal(nameof(IngestDocumentCommand.Title), error.PropertyName);
    }

    // =========================================================================
    // SourceReference validation
    // =========================================================================

    [Fact]
    public void Empty_source_reference_produces_source_reference_error()
    {
        var errors = IngestDocumentCommandValidator.Validate(ValidCommand(sourceReference: ""));

        var error = Assert.Single(errors);
        Assert.Equal(nameof(IngestDocumentCommand.SourceReference), error.PropertyName);
    }

    [Fact]
    public void Whitespace_only_source_reference_produces_source_reference_error()
    {
        var errors = IngestDocumentCommandValidator.Validate(ValidCommand(sourceReference: "\t\n"));

        var error = Assert.Single(errors);
        Assert.Equal(nameof(IngestDocumentCommand.SourceReference), error.PropertyName);
    }

    [Fact]
    public void Source_reference_at_exact_max_length_is_valid()
    {
        var reference = new string('x', IngestionLimits.MaxSourceReferenceLength);

        var errors = IngestDocumentCommandValidator.Validate(ValidCommand(sourceReference: reference));

        Assert.Empty(errors);
    }

    [Fact]
    public void Source_reference_one_character_over_max_length_produces_error()
    {
        var reference = new string('x', IngestionLimits.MaxSourceReferenceLength + 1);

        var errors = IngestDocumentCommandValidator.Validate(ValidCommand(sourceReference: reference));

        var error = Assert.Single(errors);
        Assert.Equal(nameof(IngestDocumentCommand.SourceReference), error.PropertyName);
    }

    // =========================================================================
    // SourceText validation
    // =========================================================================

    [Fact]
    public void Empty_source_text_produces_source_text_error()
    {
        var errors = IngestDocumentCommandValidator.Validate(ValidCommand(sourceText: ""));

        var error = Assert.Single(errors);
        Assert.Equal(nameof(IngestDocumentCommand.SourceText), error.PropertyName);
    }

    [Fact]
    public void Whitespace_only_source_text_produces_source_text_error()
    {
        var errors = IngestDocumentCommandValidator.Validate(ValidCommand(sourceText: "  \r\n  "));

        var error = Assert.Single(errors);
        Assert.Equal(nameof(IngestDocumentCommand.SourceText), error.PropertyName);
    }

    [Fact]
    public void Source_text_at_exact_max_length_is_valid()
    {
        var text = new string('t', IngestionLimits.MaxSourceTextLength);

        var errors = IngestDocumentCommandValidator.Validate(ValidCommand(sourceText: text));

        Assert.Empty(errors);
    }

    [Fact]
    public void Source_text_one_character_over_max_length_produces_error()
    {
        var text = new string('t', IngestionLimits.MaxSourceTextLength + 1);

        var errors = IngestDocumentCommandValidator.Validate(ValidCommand(sourceText: text));

        var error = Assert.Single(errors);
        Assert.Equal(nameof(IngestDocumentCommand.SourceText), error.PropertyName);
    }

    // =========================================================================
    // Multiple simultaneous violations
    // =========================================================================

    [Fact]
    public void All_fields_empty_produces_three_errors_one_per_property()
    {
        var command = new IngestDocumentCommand(
            Title:           "",
            SourceReference: "",
            SourceText:      "");

        var errors = IngestDocumentCommandValidator.Validate(command);

        Assert.Equal(3, errors.Count);
        Assert.Contains(errors, e => e.PropertyName == nameof(IngestDocumentCommand.Title));
        Assert.Contains(errors, e => e.PropertyName == nameof(IngestDocumentCommand.SourceReference));
        Assert.Contains(errors, e => e.PropertyName == nameof(IngestDocumentCommand.SourceText));
    }

    [Fact]
    public void Independent_field_violations_are_all_reported()
    {
        var oversizedTitle     = new string('A', IngestionLimits.MaxTitleLength + 1);
        var oversizedReference = new string('x', IngestionLimits.MaxSourceReferenceLength + 1);
        var command = new IngestDocumentCommand(
            Title:           oversizedTitle,
            SourceReference: oversizedReference,
            SourceText:      "Valid text.");

        var errors = IngestDocumentCommandValidator.Validate(command);

        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, e => e.PropertyName == nameof(IngestDocumentCommand.Title));
        Assert.Contains(errors, e => e.PropertyName == nameof(IngestDocumentCommand.SourceReference));
    }

    // =========================================================================
    // Error message content (spot-check)
    // =========================================================================

    [Fact]
    public void Empty_title_error_message_is_non_empty()
    {
        var errors = IngestDocumentCommandValidator.Validate(ValidCommand(title: ""));

        var error = Assert.Single(errors);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));
    }

    [Fact]
    public void Oversized_source_text_error_message_references_limit()
    {
        var text = new string('t', IngestionLimits.MaxSourceTextLength + 1);

        var errors = IngestDocumentCommandValidator.Validate(ValidCommand(sourceText: text));

        var error = Assert.Single(errors);
        Assert.Contains(IngestionLimits.MaxSourceTextLength.ToString(), error.Message);
    }
}
