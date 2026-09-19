using GovernmentDomainCopilot.Application.Agents.Abstractions;
using GovernmentDomainCopilot.Application.Agents.Models;
using GovernmentDomainCopilot.Application.Agents.Tools;
using GovernmentDomainCopilot.Application.Retrieval.Abstractions;
using GovernmentDomainCopilot.Application.Retrieval.Models;
using Xunit;

namespace Application.Tests.Agents;

public sealed class LookupToolFailurePrivacyTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LookupTool_WhenSearchThrows_ReturnsOnlyStableFailureCode(bool eligibility)
    {
        const string sensitiveQuery = "applicant SSN 123-45-6789";
        const string sensitiveCredential = "api-key=not-a-real-secret";
        var tool = eligibility
            ? (IAgentTool)new EligibilityLookupTool(new ThrowingSearchUseCase(sensitiveQuery, sensitiveCredential))
            : new ProcedureLookupTool(new ThrowingSearchUseCase(sensitiveQuery, sensitiveCredential));
        var context = new AgentContext(Guid.NewGuid(), "run-1", "corr-1", sensitiveQuery);
        var input = eligibility
            ? System.Text.Json.JsonSerializer.Serialize(new { topic = sensitiveQuery })
            : System.Text.Json.JsonSerializer.Serialize(new { procedure = sensitiveQuery });

        var result = await tool.ExecuteAsync(context, input, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ToolFailureCodes.ExecutionFailed, result.ErrorMessage);
        var serialisedResult = System.Text.Json.JsonSerializer.Serialize(result);
        Assert.DoesNotContain(sensitiveQuery, serialisedResult);
        Assert.DoesNotContain(sensitiveCredential, serialisedResult);
    }

    private sealed class ThrowingSearchUseCase : IHybridSearchUseCase
    {
        private readonly string _sensitiveQuery;
        private readonly string _sensitiveCredential;

        public ThrowingSearchUseCase(string sensitiveQuery, string sensitiveCredential)
        {
            _sensitiveQuery = sensitiveQuery;
            _sensitiveCredential = sensitiveCredential;
        }

        public Task<HybridSearchResponse> SearchAsync(VectorSearchRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException($"upstream failed for {_sensitiveQuery}; {_sensitiveCredential}");
    }
}
