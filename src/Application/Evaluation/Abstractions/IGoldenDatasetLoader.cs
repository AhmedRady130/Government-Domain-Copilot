namespace GovernmentDomainCopilot.Application.Evaluation.Abstractions;

using GovernmentDomainCopilot.Application.Evaluation.Models;

public interface IGoldenDatasetLoader
{
    IReadOnlyList<EvaluationCase> LoadDefault();
    IReadOnlyList<EvaluationCase> LoadFromFile(string filePath);
    IReadOnlyList<EvaluationCase> LoadFromJson(string json);
}
