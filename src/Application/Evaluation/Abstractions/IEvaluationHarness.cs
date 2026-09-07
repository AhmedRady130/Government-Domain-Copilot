namespace GovernmentDomainCopilot.Application.Evaluation.Abstractions;

using GovernmentDomainCopilot.Application.Evaluation.Models;

public interface IEvaluationHarness
{
    Task<EvaluationReport> RunAsync(
        IReadOnlyList<EvaluationCase>? cases = null,
        CancellationToken cancellationToken = default);
}
