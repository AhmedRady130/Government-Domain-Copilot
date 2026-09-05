using GovernmentDomainCopilot.Domain.Entities;

namespace GovernmentDomainCopilot.Application.Documents;

/// <summary>
/// Defines the persistence operations required by the document ingestion use case.
/// </summary>
/// <remarks>
/// This interface is defined in Application and implemented in Infrastructure.
/// It deliberately exposes only Domain entities — no EF Core types, no DbContext,
/// no IQueryable — so that Application remains infrastructure-free.
///
/// All operations are implicitly tenant-scoped: every method receives or operates on
/// entities whose <c>TenantId</c> has been set by the handler from the server-side
/// <see cref="Abstractions.ITenantContext"/>, never from caller-supplied input.
/// </remarks>
public interface IDocumentRepository
{
    /// <summary>
    /// Atomically persists a new document together with all of its chunks in a
    /// single database transaction.
    /// </summary>
    /// <remarks>
    /// Atomicity is a hard requirement: a document must never be queryable without
    /// its associated chunks, and chunks must never exist without their parent document.
    ///
    /// The implementation must verify that the <c>TenantId</c> of every
    /// <paramref name="chunks"/> element equals <c>document.TenantId</c> before
    /// writing, preventing accidental cross-tenant writes.
    /// </remarks>
    /// <param name="document">
    /// The newly created <see cref="Document"/> entity. Must not be <see langword="null"/>.
    /// </param>
    /// <param name="chunks">
    /// The ordered list of <see cref="DocumentChunk"/> entities belonging to
    /// <paramref name="document"/>. Must not be <see langword="null"/> or empty.
    /// </param>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    Task SaveAsync(
        Document document,
        IReadOnlyList<DocumentChunk> chunks,
        CancellationToken cancellationToken);
}
