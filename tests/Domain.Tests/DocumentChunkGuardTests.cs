using GovernmentDomainCopilot.Domain.Entities;

namespace Domain.Tests;

public sealed class DocumentChunkGuardTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _documentId = Guid.NewGuid();

    [Fact]
    public void DocumentChunk_allows_content_at_maximum_allowed_length()
    {
        var maxContent = new string('A', DocumentChunk.MaxContentLength);

        var chunk = new DocumentChunk(
            Guid.NewGuid(),
            _tenantId,
            _documentId,
            0,
            maxContent);

        Assert.Equal(DocumentChunk.MaxContentLength, chunk.Content.Length);
        Assert.Equal(maxContent, chunk.Content);
    }

    [Fact]
    public void DocumentChunk_rejects_content_exceeding_maximum_allowed_length()
    {
        var oversizedContent = new string('A', DocumentChunk.MaxContentLength + 1);

        var exception = Assert.Throws<ArgumentException>(() => new DocumentChunk(
            Guid.NewGuid(),
            _tenantId,
            _documentId,
            0,
            oversizedContent));

        Assert.Contains("exceeds maximum allowed length", exception.Message);
    }
}
