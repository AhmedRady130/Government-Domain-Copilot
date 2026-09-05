using GovernmentDomainCopilot.Application.Documents.Commands;
using GovernmentDomainCopilot.Application.Documents.Models;

namespace GovernmentDomainCopilot.Application.Documents;

/// <summary>
/// Application service contract for the document ingestion use case.
/// </summary>
/// <remarks>
/// Defines the full orchestration boundary:
/// validate input → obtain tenant identity from <see cref="Abstractions.ITenantContext"/>
/// → normalise text → chunk via <see cref="IDocumentChunker"/>
/// → create Domain entities → persist atomically via <see cref="IDocumentRepository"/>.
///
/// This interface is defined here so that:
/// <list type="bullet">
///   <item>The API layer depends only on Application (not Infrastructure).</item>
///   <item>The use-case can be tested in isolation by providing stub collaborators.</item>
///   <item>Future implementations (e.g. with MediatR) can satisfy this contract
///         without changing call sites.</item>
/// </list>
///
/// The implementation lives in Application (not Infrastructure), as it coordinates
/// only Domain entities and Application abstractions with no direct I/O.
/// </remarks>
public interface IIngestDocumentUseCase
{
    /// <summary>
    /// Validates, normalises, chunks, and persists the document described by
    /// <paramref name="command"/>.
    /// </summary>
    /// <param name="command">
    /// The ingestion request. Must not be <see langword="null"/>.
    /// The authoritative tenant identity is resolved internally from
    /// <see cref="Abstractions.ITenantContext"/> — it must not be supplied by the caller.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    /// <returns>
    /// An <see cref="IngestDocumentResult"/> describing the persisted document
    /// and the number of chunks created.
    /// </returns>
    /// <exception cref="Validation.IngestionValidationException">
    /// Thrown when <paramref name="command"/> fails one or more validation constraints.
    /// </exception>
    Task<IngestDocumentResult> IngestAsync(
        IngestDocumentCommand command,
        CancellationToken cancellationToken);
}
