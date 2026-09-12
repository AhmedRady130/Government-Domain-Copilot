namespace GovernmentDomainCopilot.Application.Agents.Tools;

using System.Text.Json;
using GovernmentDomainCopilot.Application.Agents.Abstractions;
using GovernmentDomainCopilot.Application.Agents.Models;
using GovernmentDomainCopilot.Application.Retrieval.Abstractions;
using GovernmentDomainCopilot.Application.Retrieval.Models;

public sealed class EligibilityLookupTool : IAgentTool
{
    private readonly IHybridSearchUseCase _hybridSearchUseCase;

    public string Name => "eligibility_lookup";
    public string Description => "Extracts and evaluates eligibility requirements, qualifying conditions, and exemptions from government regulations.";
    public bool IsSideEffecting => false;

    public EligibilityLookupTool(IHybridSearchUseCase hybridSearchUseCase)
    {
        _hybridSearchUseCase = hybridSearchUseCase ?? throw new ArgumentNullException(nameof(hybridSearchUseCase));
    }

    public async Task<ToolExecutionResult> ExecuteAsync(
        AgentContext context,
        string inputJson,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        string topic;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(inputJson) ? "{}" : inputJson);
            topic = doc.RootElement.TryGetProperty("topic", out var t)
                ? t.GetString() ?? context.UserQuery
                : context.UserQuery;
        }
        catch (JsonException)
        {
            topic = context.UserQuery;
        }

        var searchRequest = new VectorSearchRequest($"eligibility criteria conditions requirements {topic}", topK: 5);
        try
        {
            var searchResponse = await _hybridSearchUseCase.SearchAsync(searchRequest, cancellationToken);

            var evidence = searchResponse.Items.Select(i => new
            {
                source = i.SourceReference,
                title = i.Title,
                textExcerpt = i.Content.Length > 200 ? string.Concat(i.Content.AsSpan(0, 200), "...") : i.Content
            }).ToList();

            var outputJson = JsonSerializer.Serialize(new
            {
                topic,
                evidenceFound = evidence.Count > 0,
                criteria = evidence
            });

            return new ToolExecutionResult(true, outputJson);
        }
        catch (Exception ex)
        {
            return new ToolExecutionResult(false, "{}", ex.Message);
        }
    }
}
