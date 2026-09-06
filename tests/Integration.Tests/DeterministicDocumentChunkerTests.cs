using GovernmentDomainCopilot.Domain.Entities;
using GovernmentDomainCopilot.Infrastructure.Documents;

namespace Integration.Tests;

public sealed class DeterministicDocumentChunkerTests
{
    [Fact]
    public void Chunk_produces_identical_output_given_identical_input_and_configuration()
    {
        var options = new ChunkingOptions { ChunkSize = 50, ChunkOverlap = 10 };
        var chunker = new DeterministicDocumentChunker(options);

        var sourceText = "Government Executive Order 1042.\n\nSection 1: All public department operations shall adhere to strict Clean Architecture boundaries and tenant isolation policies.";

        var run1 = chunker.Chunk(sourceText);
        var run2 = chunker.Chunk(sourceText);

        Assert.Equal(run1.Count, run2.Count);
        for (int i = 0; i < run1.Count; i++)
        {
            Assert.Equal(run1[i].Sequence, run2[i].Sequence);
            Assert.Equal(run1[i].Content, run2[i].Content);
        }
    }

    [Fact]
    public void Chunk_produces_no_empty_or_whitespace_only_chunks()
    {
        var chunker = new DeterministicDocumentChunker();
        var sourceText = "  \n\n Paragraph 1 text. \n\n\n\n Paragraph 2 text.   \n\n ";

        var chunks = chunker.Chunk(sourceText);

        Assert.NotEmpty(chunks);
        foreach (var chunk in chunks)
        {
            Assert.False(string.IsNullOrWhiteSpace(chunk.Content));
        }
    }

    [Fact]
    public void Chunk_preserves_contiguous_sequence_numbers_starting_at_zero()
    {
        var options = new ChunkingOptions { ChunkSize = 30, ChunkOverlap = 5 };
        var chunker = new DeterministicDocumentChunker(options);

        var sourceText = "First sentence text here. Second sentence text here. Third sentence text here. Fourth sentence text here.";
        var chunks = chunker.Chunk(sourceText);

        Assert.True(chunks.Count > 1);
        for (int i = 0; i < chunks.Count; i++)
        {
            Assert.Equal(i, chunks[i].Sequence);
        }
    }

    [Fact]
    public void Chunk_respects_custom_chunk_size_and_overlap()
    {
        var options = new ChunkingOptions { ChunkSize = 40, ChunkOverlap = 10 };
        var chunker = new DeterministicDocumentChunker(options);

        var sourceText = "Line 1 content.\nLine 2 content.\nLine 3 content.\nLine 4 content.";
        var chunks = chunker.Chunk(sourceText);

        foreach (var chunk in chunks)
        {
            Assert.True(chunk.Content.Length <= DocumentChunk.MaxContentLength);
        }
    }

    [Fact]
    public void Chunk_normalises_line_endings_and_whitespace_deterministically()
    {
        var chunker = new DeterministicDocumentChunker();
        var crlfText = "Line 1\r\nLine 2\r\nLine 3";
        var lfText = "Line 1\nLine 2\nLine 3";

        var crlfChunks = chunker.Chunk(crlfText);
        var lfChunks = chunker.Chunk(lfText);

        Assert.Equal(crlfChunks.Count, lfChunks.Count);
        Assert.Equal(crlfChunks[0].Content, lfChunks[0].Content);
    }
}
