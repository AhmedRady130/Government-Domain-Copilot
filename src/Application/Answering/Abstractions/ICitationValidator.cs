namespace GovernmentDomainCopilot.Application.Answering.Abstractions;

using GovernmentDomainCopilot.Application.Answering.Models;

public interface ICitationValidator
{
    CitationValidationResult Validate(string text, IReadOnlyList<CitationItem> availableCitations);
}
