namespace Application.Tests.Documents.Security;

using GovernmentDomainCopilot.Application.Documents.Security;

public sealed class RegexPiiRedactorTests
{
    private readonly RegexPiiRedactor _sut = new();

    [Fact]
    public void Redact_ReplacesEmailPhoneAndNationalIdWithoutRetainingValues()
    {
        const string source = "Contact citizen@example.test on +20 10 1234 5678. National ID: 29801011234567.";

        var result = _sut.Redact(source);

        Assert.True(result.HasPii);
        Assert.Equal(1, result.EmailCount);
        Assert.Equal(1, result.PhoneNumberCount);
        Assert.Equal(1, result.NationalIdCount);
        Assert.Contains("[REDACTED:EMAIL]", result.RedactedText);
        Assert.Contains("[REDACTED:PHONE]", result.RedactedText);
        Assert.Contains("[REDACTED:NATIONAL_ID]", result.RedactedText);
        Assert.DoesNotContain("citizen@example.test", result.RedactedText);
        Assert.DoesNotContain("+20 10 1234 5678", result.RedactedText);
        Assert.DoesNotContain("29801011234567", result.RedactedText);
    }

    [Fact]
    public void Redact_DoesNotModifyTextWithoutRecognizedPii()
    {
        const string source = "The permit application requires documentary evidence.";

        var result = _sut.Redact(source);

        Assert.False(result.HasPii);
        Assert.Equal(source, result.RedactedText);
    }
}
