using System.Text.Json;

namespace GovernmentDomainCopilot.Application.Corpus;

/// <summary>Committed synthetic corpus metadata. Tenant IDs are manifest-controlled and never supplied by CLI users.</summary>
public sealed record CorpusManifest(int RequiredDocumentCount, int RequiredPageCount, IReadOnlyList<CorpusDocument> Documents)
{
    public static CorpusManifest Parse(string json) =>
        JsonSerializer.Deserialize<CorpusManifest>(json, SerializerOptions)
        ?? throw new InvalidOperationException("Corpus manifest is empty or invalid.");

    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNameCaseInsensitive = true };
}

public sealed record CorpusDocument(
    string Id,
    string Title,
    string TenantId,
    string Category,
    string SourceReference,
    string File,
    int PageCount,
    bool IsSynthetic);
