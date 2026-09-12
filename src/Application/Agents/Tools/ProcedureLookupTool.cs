namespace GovernmentDomainCopilot.Application.Agents.Tools;

using System.Text.Json;
using GovernmentDomainCopilot.Application.Agents.Abstractions;
using GovernmentDomainCopilot.Application.Agents.Models;
using GovernmentDomainCopilot.Application.Retrieval.Abstractions;
using GovernmentDomainCopilot.Application.Retrieval.Models;

public sealed class ProcedureLookupTool : IAgentTool
{
    private readonly IHybridSearchUseCase _hybridSearchUseCase;

    public string Name => "procedure_lookup";
    public string Description => "Looks up procedural workflows, statutory steps, required forms, fees, deadlines, and authority routing.";
    public bool IsSideEffecting => false;

    public ProcedureLookupTool(IHybridSearchUseCase hybridSearchUseCase)
    {
        _hybridSearchUseCase = hybridSearchUseCase ?? throw new ArgumentNullException(nameof(hybridSearchUseCase));
    }

    public async Task<ToolExecutionResult> ExecuteAsync(
        AgentContext context,
        string inputJson,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        string procedureName;
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(inputJson) ? "{}" : inputJson);
            procedureName = doc.RootElement.TryGetProperty("procedure", out var p)
                ? p.GetString() ?? context.UserQuery
                : context.UserQuery;
        }
        catch (JsonException)
        {
            procedureName = context.UserQuery;
        }

        var searchRequest = new VectorSearchRequest($"procedure steps timeline fee deadline submission {procedureName}", topK: 5);
        try
        {
            var searchResponse = await _hybridSearchUseCase.SearchAsync(searchRequest, cancellationToken);

            var steps = searchResponse.Items.Select(i => new
            {
                source = i.SourceReference,
                title = i.Title,
                textExcerpt = i.Content.Length > 200 ? string.Concat(i.Content.AsSpan(0, 200), "...") : i.Content
            }).ToList();

            var outputJson = JsonSerializer.Serialize(new
            {
                procedure = procedureName,
                stepsFound = steps.Count > 0,
                proceduralEvidence = steps
            });

            return new ToolExecutionResult(true, outputJson);
        }
        catch (Exception ex)
        {
            return new ToolExecutionResult(false, "{}", ex.Message);
        }
    }
}
