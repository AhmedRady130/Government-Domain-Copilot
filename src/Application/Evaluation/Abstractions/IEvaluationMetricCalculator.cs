namespace GovernmentDomainCopilot.Application.Evaluation.Abstractions;

using GovernmentDomainCopilot.Application.Answering.Models;
using GovernmentDomainCopilot.Application.Evaluation.Models;

public interface IEvaluationMetricCalculator
{
    EvaluationCaseResult EvaluateCase(
        EvaluationCase evaluationCase,
        GroundedAnswerResponse actualResponse,
        TimeSpan duration);

    EvaluationReport CalculateReport(
        IReadOnlyList<EvaluationCaseResult> caseResults,
        TimeSpan totalDuration);
}
