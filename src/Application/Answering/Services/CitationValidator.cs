namespace GovernmentDomainCopilot.Application.Answering.Services;

using System.Text.RegularExpressions;
using GovernmentDomainCopilot.Application.Answering.Abstractions;
using GovernmentDomainCopilot.Application.Answering.Models;

public sealed partial class CitationValidator : ICitationValidator
{
    private static readonly Regex CitationRegex = MyCitationRegex();

    public CitationValidationResult Validate(string text, IReadOnlyList<CitationItem> availableCitations)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new CitationValidationResult(false, string.Empty, Array.Empty<CitationItem>(), "Empty or null completion text.");
        }

        string trimmed = text.Trim();

        // Check if model explicitly indicated refusal
        if (trimmed.Contains("Insufficient evidence in the available corpus", StringComparison.OrdinalIgnoreCase))
        {
            return new CitationValidationResult(false, trimmed, Array.Empty<CitationItem>(), "Model indicated insufficient evidence.");
        }

        var matches = CitationRegex.Matches(trimmed);
        if (matches.Count == 0)
        {
            return new CitationValidationResult(false, trimmed, Array.Empty<CitationItem>(), "No citation markers found in completion text.");
        }

        var availableMap = availableCitations.ToDictionary(c => c.CitationId, StringComparer.Ordinal);
        var matchedCitations = new LinkedHashSetCitation();
        bool foundInvalidCitation = false;

        foreach (Match match in matches)
        {
            string citationId = match.Value; // e.g. "[1]"
            if (availableMap.TryGetValue(citationId, out var citation))
            {
                matchedCitations.Add(citation);
            }
            else
            {
                foundInvalidCitation = true;
            }
        }

        if (foundInvalidCitation && matchedCitations.Count == 0)
        {
            return new CitationValidationResult(false, trimmed, Array.Empty<CitationItem>(), "Completion referenced citation IDs outside the retrieved evidence set.");
        }

        if (matchedCitations.Count == 0)
        {
            return new CitationValidationResult(false, trimmed, Array.Empty<CitationItem>(), "No valid citations could be matched to retrieved evidence.");
        }

        // If there were invalid citations mixed in with valid ones, filter out invalid ones or reject if unsafe.
        // For structural safety: if any cited ID is outside retrieved set, we reject or sanitize. If foundInvalidCitation is true, reject.
        if (foundInvalidCitation)
        {
            return new CitationValidationResult(false, trimmed, Array.Empty<CitationItem>(), "Completion contained citations to non-retrieved source IDs.");
        }

        return new CitationValidationResult(true, trimmed, matchedCitations.ToList());
    }

    [GeneratedRegex(@"\[\d+\]")]
    private static partial Regex MyCitationRegex();

    private sealed class LinkedHashSetCitation
    {
        private readonly List<CitationItem> _list = new();
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

        public void Add(CitationItem item)
        {
            if (_seen.Add(item.CitationId))
            {
                _list.Add(item);
            }
        }

        public int Count => _list.Count;
        public List<CitationItem> ToList() => _list;
    }
}
