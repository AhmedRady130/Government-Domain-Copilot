namespace GovernmentDomainCopilot.Application.Agents.Tools;

using System.Text.Json;
using GovernmentDomainCopilot.Application.Agents.Abstractions;
using GovernmentDomainCopilot.Application.Agents.Models;
using GovernmentDomainCopilot.Application.Retrieval.Abstractions;
using GovernmentDomainCopilot.Application.Retrieval.Models;

public sealed class DocumentSearchTool : IAgentTool
{
    private readonly IHybridSearchUseCase _hybridSearchUseCase;

    public string Name => "document_search";
    public string Description => "Searches the tenant's ingested government documents using hybrid vector and keyword retrieval.";
    public bool IsSideEffecting => false;

    public DocumentSearchTool(IHybridSearchUseCase hybridSearchUseCase)
    {
        _hybridSearchUseCase = hybridSearchUseCase ?? throw new ArgumentNullException(nameof(hybridSearchUseCase));
    }

    public async Task<ToolExecutionResult> ExecuteAsync(
        AgentContext context,
        string inputJson,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        string query;
        int topK = 5;

        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(inputJson) ? "{}" : inputJson);
            var root = doc.RootElement;
            query = root.TryGetProperty("query", out var q) ? q.GetString() ?? context.UserQuery : context.UserQuery;
            if (root.TryGetProperty("topK", out var k) && k.TryGetInt32(out var parsedK))
            {
                topK = Math.Clamp(parsedK, 1, 20);
            }
        }
        catch (JsonException)
        {
            query = context.UserQuery;
        }

        try
        {
            var searchRequest = new VectorSearchRequest(query, topK);
            var searchResponse = await _hybridSearchUseCase.SearchAsync(searchRequest, cancellationToken);

            var items = searchResponse.Items.Select(item => new
            {
                chunkId = item.ChunkId,
                documentId = item.DocumentId,
                sourceReference = item.SourceReference,
                title = item.Title,
                contentExcerpt = item.Content.Length > 200 ? string.Concat(item.Content.AsSpan(0, 200), "...") : item.Content,
                score = item.RerankScore
            }).ToList();

            var outputJson = JsonSerializer.Serialize(new
            {
                count = items.Count,
                results = items
            });

            return new ToolExecutionResult(true, outputJson);
        }
        catch (Exception ex)
        {
            return new ToolExecutionResult(false, "{}", ex.Message);
        }
    }
}
