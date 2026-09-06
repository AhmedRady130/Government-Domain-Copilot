using GovernmentDomainCopilot.Application.Documents;
using GovernmentDomainCopilot.Application.Documents.Models;
using GovernmentDomainCopilot.Domain.Entities;
using Microsoft.Extensions.Options;

namespace GovernmentDomainCopilot.Infrastructure.Documents;

/// <summary>
/// Infrastructure implementation of <see cref="IDocumentChunker"/> supplying deterministic,
/// boundary-aware sliding-window chunking over normalised source text.
/// </summary>
public sealed class DeterministicDocumentChunker : IDocumentChunker
{
    private readonly ChunkingOptions _options;

    public DeterministicDocumentChunker(IOptions<ChunkingOptions>? options = null)
    {
        _options = options?.Value ?? new ChunkingOptions();

        if (_options.ChunkOverlap >= _options.ChunkSize)
        {
            throw new InvalidOperationException(
                $"ChunkOverlap ({_options.ChunkOverlap}) must be strictly less than ChunkSize ({_options.ChunkSize}).");
        }
    }

    public DeterministicDocumentChunker(ChunkingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;

        if (_options.ChunkOverlap >= _options.ChunkSize)
        {
            throw new InvalidOperationException(
                $"ChunkOverlap ({_options.ChunkOverlap}) must be strictly less than ChunkSize ({_options.ChunkSize}).");
        }
    }

    public IReadOnlyList<ChunkData> Chunk(string normalizedText)
    {
        if (string.IsNullOrWhiteSpace(normalizedText))
        {
            throw new ArgumentException(
                "Input text cannot be null, empty, or whitespace-only.",
                nameof(normalizedText));
        }

        var text = DeterministicTextNormalizer.Normalize(normalizedText);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException(
                "Input text contained no content after normalisation.",
                nameof(normalizedText));
        }

        if (text.Length <= _options.ChunkSize)
        {
            return new[] { new ChunkData(0, text) };
        }

        var chunks = new List<ChunkData>();
        int sequence = 0;
        int start = 0;
        int textLength = text.Length;

        while (start < textLength)
        {
            int end = Math.Min(start + _options.ChunkSize, textLength);

            if (end < textLength)
            {
                int boundary = FindNaturalBoundary(text, start, end);
                if (boundary > start)
                {
                    end = boundary;
                }
            }

            string content = text.Substring(start, end - start).Trim();
            if (!string.IsNullOrWhiteSpace(content))
            {
                if (content.Length > DocumentChunk.MaxContentLength)
                {
                    content = content.Substring(0, DocumentChunk.MaxContentLength).Trim();
                }

                chunks.Add(new ChunkData(sequence++, content));
            }

            if (end >= textLength)
            {
                break;
            }

            int nextStart = end - _options.ChunkOverlap;
            if (nextStart <= start)
            {
                nextStart = end;
            }

            start = nextStart;
        }

        return chunks.AsReadOnly();
    }

    private int FindNaturalBoundary(string text, int start, int maxEnd)
    {
        int minSearchIndex = Math.Max(start + 1, maxEnd - _options.ChunkOverlap);

        // 1. Look for double newline (paragraph boundary)
        for (int i = maxEnd - 1; i >= minSearchIndex; i--)
        {
            if (i > start && text[i] == '\n' && text[i - 1] == '\n')
            {
                return i + 1;
            }
        }

        // 2. Look for single newline
        for (int i = maxEnd - 1; i >= minSearchIndex; i--)
        {
            if (text[i] == '\n')
            {
                return i + 1;
            }
        }

        // 3. Look for sentence termination punctuation (. ! ?) followed by space
        for (int i = maxEnd - 1; i >= minSearchIndex; i--)
        {
            if ((text[i] == '.' || text[i] == '!' || text[i] == '?') && i + 1 < textLength(text) && char.IsWhiteSpace(text[i + 1]))
            {
                return i + 1;
            }
        }

        // 4. Look for whitespace boundary
        for (int i = maxEnd - 1; i >= minSearchIndex; i--)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                return i + 1;
            }
        }

        return maxEnd;
    }

    private static int textLength(string text) => text.Length;
}
