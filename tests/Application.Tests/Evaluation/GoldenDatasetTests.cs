namespace Application.Tests.Evaluation;

using System.Text.Json;
using GovernmentDomainCopilot.Application.Evaluation.Abstractions;
using GovernmentDomainCopilot.Application.Evaluation.Models;
using GovernmentDomainCopilot.Application.Evaluation.Services;

public sealed class GoldenDatasetTests
{
    private readonly IGoldenDatasetLoader _sut = new GoldenDatasetLoader();

    [Fact]
    public void LoadDefault_ReturnsNonEmptyDataset()
    {
        var cases = _sut.LoadDefault();
        Assert.NotNull(cases);
        Assert.NotEmpty(cases);
    }

    [Fact]
    public void LoadDefault_DatasetContainsAtLeast25Cases()
    {
        var cases = _sut.LoadDefault();
        Assert.True(cases.Count >= 25,
            $"Expected >= 25 golden cases but found {cases.Count}.");
    }

    [Fact]
    public void LoadDefault_DatasetContainsAtLeast5AdversarialCases()
    {
        var cases = _sut.LoadDefault();
        int adversarialCount = cases.Count(c => c.IsAdversarial);
        Assert.True(adversarialCount >= 5,
            $"Expected >= 5 adversarial cases but found {adversarialCount}.");
    }

    [Fact]
    public void LoadDefault_ContainsAtLeastThreeInstructionAttackCasesThatRequireRefusal()
    {
        var cases = _sut.LoadDefault();
        var instructionAttacks = cases
            .Where(c => c.AdversarialType is "PromptInjection" or "IndirectPromptInjection" or "SecretExfiltration")
            .ToList();

        Assert.True(instructionAttacks.Count >= 3,
            $"Expected >= 3 prompt-injection/instruction-attack cases but found {instructionAttacks.Count}.");
        Assert.All(instructionAttacks, c => Assert.True(c.ExpectRefusal, $"Instruction attack '{c.Id}' must require refusal."));
        Assert.Equal(instructionAttacks.Count, instructionAttacks.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void LoadDefault_AllCasesHaveRequiredFields()
    {
        var cases = _sut.LoadDefault();
        foreach (var c in cases)
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Id), $"Case has empty Id: {c}");
            Assert.False(string.IsNullOrWhiteSpace(c.Query), $"Case '{c.Id}' has empty Query.");
            Assert.NotEqual(Guid.Empty, c.TenantId);
            Assert.False(string.IsNullOrWhiteSpace(c.Category), $"Case '{c.Id}' has empty Category.");
            Assert.NotNull(c.ExpectedSourceReferences);
            Assert.NotNull(c.ExpectedKeywords);
        }
    }

    [Fact]
    public void LoadDefault_AllCaseIdsAreUnique()
    {
        var cases = _sut.LoadDefault();
        var ids = cases.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Equal(cases.Count, ids.Count);
    }

    [Fact]
    public void LoadDefault_AdversarialCasesHaveAdversarialTypeSet()
    {
        var cases = _sut.LoadDefault();
        foreach (var c in cases.Where(c => c.IsAdversarial))
        {
            Assert.False(string.IsNullOrWhiteSpace(c.AdversarialType),
                $"Adversarial case '{c.Id}' is missing AdversarialType.");
        }
    }

    [Fact]
    public void LoadDefault_DatasetContainsBothRefusalAndAnswerableCases()
    {
        var cases = _sut.LoadDefault();
        Assert.Contains(cases, c => c.ExpectRefusal);
        Assert.Contains(cases, c => !c.ExpectRefusal);
    }

    [Fact]
    public void LoadFromJson_ValidJson_ReturnsExpectedCases()
    {
        var json = JsonSerializer.Serialize(new[]
        {
            new
            {
                id = "test-001",
                tenantId = Guid.NewGuid(),
                category = "TestCategory",
                query = "Sample query?",
                expectRefusal = false,
                expectedSourceReferences = new[] { "ref-1" },
                expectedKeywords = new[] { "keyword" },
                isAdversarial = false
            }
        });

        var cases = _sut.LoadFromJson(json);
        Assert.Single(cases);
        Assert.Equal("test-001", cases[0].Id);
        Assert.Equal("Sample query?", cases[0].Query);
    }

    [Fact]
    public void LoadFromJson_EmptyArray_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() => _sut.LoadFromJson("[]"));
    }

    [Fact]
    public void LoadFromJson_MissingId_ThrowsInvalidOperationException()
    {
        var json = JsonSerializer.Serialize(new[]
        {
            new { id = "", tenantId = Guid.NewGuid(), category = "Cat", query = "Q?", expectRefusal = false,
                  expectedSourceReferences = Array.Empty<string>(), expectedKeywords = Array.Empty<string>(), isAdversarial = false }
        });
        Assert.Throws<InvalidOperationException>(() => _sut.LoadFromJson(json));
    }

    [Fact]
    public void LoadFromJson_EmptyTenantId_ThrowsInvalidOperationException()
    {
        var json = JsonSerializer.Serialize(new[]
        {
            new { id = "x-001", tenantId = Guid.Empty, category = "Cat", query = "Q?", expectRefusal = false,
                  expectedSourceReferences = Array.Empty<string>(), expectedKeywords = Array.Empty<string>(), isAdversarial = false }
        });
        Assert.Throws<InvalidOperationException>(() => _sut.LoadFromJson(json));
    }

    [Fact]
    public void LoadFromJson_MissingQuery_ThrowsInvalidOperationException()
    {
        var json = JsonSerializer.Serialize(new[]
        {
            new { id = "x-001", tenantId = Guid.NewGuid(), category = "Cat", query = "", expectRefusal = false,
                  expectedSourceReferences = Array.Empty<string>(), expectedKeywords = Array.Empty<string>(), isAdversarial = false }
        });
        Assert.Throws<InvalidOperationException>(() => _sut.LoadFromJson(json));
    }

    [Fact]
    public void LoadFromJson_InvalidJson_ThrowsJsonException()
    {
        Assert.ThrowsAny<Exception>(() => _sut.LoadFromJson("{ not valid json"));
    }

    [Fact]
    public void LoadFromFile_NonExistentPath_ThrowsFileNotFoundException()
    {
        Assert.Throws<FileNotFoundException>(() => _sut.LoadFromFile("/nonexistent/path/dataset.json"));
    }
}
