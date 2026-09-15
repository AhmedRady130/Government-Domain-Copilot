namespace GovernmentDomainCopilot.Application.Corpus;

/// <summary>Validates committed corpus metadata and explicit form-feed page delimiters.</summary>
public static class CorpusValidator
{
    public const int AssessmentMinimumDocuments = 30;
    public const int AssessmentMinimumPages = 150;
    public const int MinimumCharactersPerPage = 800;

    public static CorpusValidationResult Validate(CorpusManifest manifest, Func<string, string> readFile)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(readFile);
        var errors = new List<string>();
        if (manifest.RequiredDocumentCount < AssessmentMinimumDocuments) errors.Add("Manifest document threshold is below assessment minimum of 30.");
        if (manifest.RequiredPageCount < AssessmentMinimumPages) errors.Add("Manifest page threshold is below assessment minimum of 150.");
        if (manifest.Documents.Count < manifest.RequiredDocumentCount) errors.Add("Corpus does not meet its document threshold.");
        if (manifest.Documents.Select(d => d.Id).Distinct(StringComparer.Ordinal).Count() != manifest.Documents.Count) errors.Add("Duplicate document IDs found.");
        if (manifest.Documents.Select(d => d.SourceReference).Distinct(StringComparer.Ordinal).Count() != manifest.Documents.Count) errors.Add("Duplicate source references found.");

        var pages = 0;
        foreach (var document in manifest.Documents)
        {
            if (string.IsNullOrWhiteSpace(document.Id) || string.IsNullOrWhiteSpace(document.Title) || string.IsNullOrWhiteSpace(document.TenantId) || string.IsNullOrWhiteSpace(document.Category) || string.IsNullOrWhiteSpace(document.SourceReference) || string.IsNullOrWhiteSpace(document.File))
                errors.Add($"Document '{document.Id}' has missing required metadata.");
            if (!document.IsSynthetic) errors.Add($"Document '{document.Id}' is not marked synthetic.");
            var content = readFile(document.File);
            if (!content.Contains("Synthetic training/demo data — not an official government publication.", StringComparison.Ordinal)) errors.Add($"Document '{document.Id}' lacks the synthetic-data disclaimer.");
            var actualPages = content.Split('\f').Length;
            if (actualPages != document.PageCount) errors.Add($"Document '{document.Id}' declares {document.PageCount} pages but contains {actualPages} explicit pages.");
            if (content.Split('\f').Any(page => page.Trim().Length < MinimumCharactersPerPage)) errors.Add($"Document '{document.Id}' contains an insubstantial page.");
            pages += actualPages;
        }
        if (pages < manifest.RequiredPageCount) errors.Add("Corpus does not meet its page threshold.");
        return new CorpusValidationResult(manifest.Documents.Count, pages, manifest.Documents.GroupBy(d => d.TenantId).ToDictionary(g => g.Key, g => g.Count()), manifest.Documents.GroupBy(d => Path.GetExtension(d.File)).ToDictionary(g => g.Key, g => g.Count()), errors);
    }
}

public sealed record CorpusValidationResult(int DocumentCount, int PageCount, IReadOnlyDictionary<string, int> TenantDistribution, IReadOnlyDictionary<string, int> FormatDistribution, IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}
