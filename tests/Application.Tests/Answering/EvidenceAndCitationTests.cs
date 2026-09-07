using GovernmentDomainCopilot.Application.Answering.Models;
using GovernmentDomainCopilot.Application.Answering.Services;
using GovernmentDomainCopilot.Application.Retrieval.Models;
using Xunit;

namespace Application.Tests.Answering;

public sealed class EvidenceSufficiencyPolicyTests
{
    private readonly EvidenceSufficiencyPolicy _sut = new(minRerankScoreThreshold: 0.15);

    [Fact]
    public void Evaluate_NullOrEmptyCandidates_ReturnsInsufficient()
    {
        var resultNull = _sut.Evaluate(null!);
        Assert.False(resultNull.IsSufficient);
        Assert.NotNull(resultNull.RefusalReason);

        var resultEmpty = _sut.Evaluate(Array.Empty<RerankResultItem>());
        Assert.False(resultEmpty.IsSufficient);
        Assert.NotNull(resultEmpty.RefusalReason);
    }

    [Fact]
    public void Evaluate_LowRerankScore_ReturnsInsufficient()
    {
        var lowScoreCandidate = new RerankResultItem(
            Guid.NewGuid(), Guid.NewGuid(), 0, "Title", "ref", "Content", 0.9, 0.1, 0.005, 1, 0.10, 1);

        var result = _sut.Evaluate(new[] { lowScoreCandidate });

        Assert.False(result.IsSufficient);
        Assert.NotNull(result.RefusalReason);
    }

    [Fact]
    public void Evaluate_SufficientScore_ReturnsSufficient()
    {
        var highScoreCandidate = new RerankResultItem(
            Guid.NewGuid(), Guid.NewGuid(), 0, "Title", "ref", "Content", 0.1, 0.9, 0.032, 1, 0.85, 1);

        var result = _sut.Evaluate(new[] { highScoreCandidate });

        Assert.True(result.IsSufficient);
        Assert.Null(result.RefusalReason);
    }
}

public sealed class CitationValidatorTests
{
    private readonly CitationValidator _sut = new();

    private readonly CitationItem _cit1 = new("[1]", Guid.NewGuid(), Guid.NewGuid(), "ref-1", "Doc 1", 0);
    private readonly CitationItem _cit2 = new("[2]", Guid.NewGuid(), Guid.NewGuid(), "ref-2", "Doc 2", 1);

    [Fact]
    public void Validate_ValidCitations_ReturnsValid()
    {
        string text = "Procurement tenders require a 500 EGP filing fee [1]. Submissions close at noon [2].";
        var available = new[] { _cit1, _cit2 };

        var result = _sut.Validate(text, available);

        Assert.True(result.IsValid);
        Assert.Equal(2, result.ValidCitations.Count);
        Assert.Equal("[1]", result.ValidCitations[0].CitationId);
        Assert.Equal("[2]", result.ValidCitations[1].CitationId);
    }

    [Fact]
    public void Validate_InvalidCitationIdOutsideSet_ReturnsInvalid()
    {
        string text = "Tenders require filing fees [99].";
        var available = new[] { _cit1, _cit2 };

        var result = _sut.Validate(text, available);

        Assert.False(result.IsValid);
        Assert.Empty(result.ValidCitations);
        Assert.NotNull(result.FailureReason);
    }

    [Fact]
    public void Validate_NoCitationsInOutput_ReturnsInvalid()
    {
        string text = "Tenders require filing fees without any citations.";
        var available = new[] { _cit1, _cit2 };

        var result = _sut.Validate(text, available);

        Assert.False(result.IsValid);
        Assert.Empty(result.ValidCitations);
        Assert.NotNull(result.FailureReason);
    }

    [Fact]
    public void Validate_ExplicitRefusalText_ReturnsInvalid()
    {
        string text = "Insufficient evidence in the available corpus to answer this question reliably.";
        var available = new[] { _cit1 };

        var result = _sut.Validate(text, available);

        Assert.False(result.IsValid);
        Assert.Empty(result.ValidCitations);
    }
}

public sealed class EvidenceContextBuilderTests
{
    [Fact]
    public void BuildContext_OversizedFirstChunk_OutputNeverExceedsMaxContextCharacters()
    {
        string oversizedContent = new string('A', 15_000);
        var chunk = new RerankResultItem(
            Guid.NewGuid(), Guid.NewGuid(), 0, "Oversized Decree", "ref-oversized", oversizedContent, 0.1, 0.9, 0.032, 1, 0.85, 1);

        var (formattedContext, citations) = EvidenceContextBuilder.BuildContext(new[] { chunk });

        Assert.NotEmpty(formattedContext);
        Assert.True(formattedContext.Length <= EvidenceContextBuilder.MaxContextCharacters,
            $"FormattedContext length {formattedContext.Length} exceeded MaxContextCharacters {EvidenceContextBuilder.MaxContextCharacters}");
        Assert.Single(citations);
        Assert.Equal("[1]", citations[0].CitationId);
    }

    [Fact]
    public void BuildContext_MultipleChunks_RespectsBudgetAndCitationsMatchIncludedChunks()
    {
        var chunks = Enumerable.Range(1, 10).Select(i => new RerankResultItem(
            Guid.NewGuid(), Guid.NewGuid(), i, $"Decree {i}", $"ref-{i}", new string('X', 3_000), 0.1, 0.9, 0.032, i, 0.85, i)).ToList();

        var (formattedContext, citations) = EvidenceContextBuilder.BuildContext(chunks);

        Assert.True(formattedContext.Length <= EvidenceContextBuilder.MaxContextCharacters);
        Assert.NotEmpty(citations);
        foreach (var cit in citations)
        {
            Assert.Contains(cit.CitationId, formattedContext);
        }
    }
}
