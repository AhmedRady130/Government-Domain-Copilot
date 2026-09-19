namespace GovernmentDomainCopilot.Application.Documents.Security;

/// <summary>Detects common PII and returns text that is safe to persist in the document corpus.</summary>
public interface IPiiRedactor
{
    PiiRedactionResult Redact(string? input);
}
