namespace GovernmentDomainCopilot.Application.Documents.Security;

/// <summary>Safe PII-redaction outcome. It intentionally contains no matched values.</summary>
public sealed record PiiRedactionResult(
    string RedactedText,
    int EmailCount,
    int PhoneNumberCount,
    int NationalIdCount)
{
    public bool HasPii => EmailCount + PhoneNumberCount + NationalIdCount > 0;
}
