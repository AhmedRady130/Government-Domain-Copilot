namespace GovernmentDomainCopilot.Application.Documents.Security;

using System.Text.RegularExpressions;

/// <summary>
/// A deliberately small, deterministic first-line PII redactor for raw document ingestion.
/// It does not retain matched values; callers can safely audit only the returned counts.
/// </summary>
public sealed partial class RegexPiiRedactor : IPiiRedactor
{
    public PiiRedactionResult Redact(string? input)
    {
        var text = input ?? string.Empty;
        var emailCount = EmailPattern().Count(text);
        var nationalIdCount = NationalIdPattern().Count(text);

        text = EmailPattern().Replace(text, "[REDACTED:EMAIL]");
        text = NationalIdPattern().Replace(text, "[REDACTED:NATIONAL_ID]");
        var phoneNumberCount = PhonePattern().Count(text);
        text = PhonePattern().Replace(text, "[REDACTED:PHONE]");

        return new PiiRedactionResult(text, emailCount, phoneNumberCount, nationalIdCount);
    }

    [GeneratedRegex(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    // Egyptian national IDs are 14 digits. Keeping this distinct from telephone matching prevents overlap.
    [GeneratedRegex(@"(?<!\d)\d{14}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex NationalIdPattern();

    // Broad international phone shape, deliberately requiring at least eight digits.
    [GeneratedRegex(@"(?<![\w\d])(?:\+?\d[\d\s().-]{6,}\d)(?![\w\d])", RegexOptions.CultureInvariant)]
    private static partial Regex PhonePattern();
}
