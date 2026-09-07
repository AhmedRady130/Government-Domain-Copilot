namespace GovernmentDomainCopilot.Application.Answering.Models;

public sealed record CitationItem(
    string CitationId,
    Guid ChunkId,
    Guid DocumentId,
    string SourceReference,
    string Title,
    int Sequence);
