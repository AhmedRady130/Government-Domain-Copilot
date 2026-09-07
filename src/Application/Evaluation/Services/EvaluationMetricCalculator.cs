namespace GovernmentDomainCopilot.Application.Evaluation.Services;

using GovernmentDomainCopilot.Application.Answering.Models;
using GovernmentDomainCopilot.Application.Evaluation.Abstractions;
using GovernmentDomainCopilot.Application.Evaluation.Models;

public sealed class EvaluationMetricCalculator : IEvaluationMetricCalculator
{
    public EvaluationCaseResult EvaluateCase(
        EvaluationCase evaluationCase,
        GroundedAnswerResponse actualResponse,
        TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(evaluationCase);
        ArgumentNullException.ThrowIfNull(actualResponse);

        bool refusalCorrect;
        bool retrievalHit;
        bool grounded;
        string? failureReason = null;

        // Metric 1: Refusal Correctness
        if (evaluationCase.ExpectRefusal)
        {
            refusalCorrect = actualResponse.Status == GroundedAnswerStatus.Refused;
            if (!refusalCorrect)
            {
                failureReason = $"Expected Refused status but received Grounded answer.";
            }
        }
        else
        {
            refusalCorrect = actualResponse.Status == GroundedAnswerStatus.Grounded;
            if (!refusalCorrect)
            {
                failureReason = $"Expected Grounded answer but received Refused (Reason: {actualResponse.Reason ?? "None"}).";
            }
        }

        // Metric 2: Retrieval Hit Rate
        if (evaluationCase.ExpectRefusal)
        {
            // For refusal cases, retrieval hit is true if no hallucinated/unsupported citations were produced
            retrievalHit = actualResponse.Status == GroundedAnswerStatus.Refused;
        }
        else
        {
            if (evaluationCase.ExpectedSourceReferences.Count == 0)
            {
                retrievalHit = actualResponse.Citations.Count > 0;
            }
            else
            {
                retrievalHit = actualResponse.Citations.Any(c =>
                    evaluationCase.ExpectedSourceReferences.Any(expected =>
                        string.Equals(c.SourceReference, expected, StringComparison.OrdinalIgnoreCase)));
            }

            if (!retrievalHit && refusalCorrect)
            {
                failureReason = $"Expected source references [{string.Join(", ", evaluationCase.ExpectedSourceReferences)}] were not present in citations.";
            }
        }

        // Metric 3: Groundedness
        if (actualResponse.Status == GroundedAnswerStatus.Refused)
        {
            // Refusal is grounded if refusal was expected
            grounded = evaluationCase.ExpectRefusal;
        }
        else
        {
            // Grounded answer must have non-empty answer and valid citations
            bool hasValidCitations = actualResponse.Citations.Count > 0;
            bool hasNonEmptyAnswer = !string.IsNullOrWhiteSpace(actualResponse.Answer);
            grounded = hasValidCitations && hasNonEmptyAnswer && !evaluationCase.ExpectRefusal;

            if (!grounded && failureReason == null)
            {
                failureReason = evaluationCase.ExpectRefusal
                    ? "Generated answer for an ungroundable or adversarial case."
                    : "Answer is missing citations or content.";
            }
        }

        // Keyword verification for answerable cases
        bool keywordsMatched = true;
        if (!evaluationCase.ExpectRefusal && evaluationCase.ExpectedKeywords.Count > 0 && actualResponse.Status == GroundedAnswerStatus.Grounded)
        {
            var answerText = actualResponse.Answer ?? string.Empty;
            keywordsMatched = evaluationCase.ExpectedKeywords.Any(k =>
                answerText.Contains(k, StringComparison.OrdinalIgnoreCase));

            if (!keywordsMatched && failureReason == null)
            {
                failureReason = $"Answer did not contain any expected keywords: [{string.Join(", ", evaluationCase.ExpectedKeywords)}].";
            }
        }

        bool passed = refusalCorrect && retrievalHit && grounded && keywordsMatched;

        return new EvaluationCaseResult(
            evaluationCase.Id,
            passed,
            retrievalHit,
            grounded,
            refusalCorrect,
            actualResponse.Status,
            actualResponse.Answer,
            actualResponse.Reason,
            actualResponse.Citations,
            duration,
            passed ? null : failureReason);
    }

    public EvaluationReport CalculateReport(
        IReadOnlyList<EvaluationCaseResult> caseResults,
        TimeSpan totalDuration)
    {
        ArgumentNullException.ThrowIfNull(caseResults);

        int totalCases = caseResults.Count;
        if (totalCases == 0)
        {
            return new EvaluationReport(0, 0, 0, 0.0, 0.0, 0.0, totalDuration, Array.Empty<EvaluationCaseResult>(), DateTimeOffset.UtcNow);
        }

        int passedCases = caseResults.Count(r => r.Passed);
        int failedCases = totalCases - passedCases;

        double retrievalHitRate = (double)caseResults.Count(r => r.RetrievalHit) / totalCases;
        double groundednessScore = (double)caseResults.Count(r => r.Grounded) / totalCases;
        double refusalCorrectness = (double)caseResults.Count(r => r.RefusalCorrect) / totalCases;

        return new EvaluationReport(
            totalCases,
            passedCases,
            failedCases,
            Math.Round(retrievalHitRate, 4),
            Math.Round(groundednessScore, 4),
            Math.Round(refusalCorrectness, 4),
            totalDuration,
            caseResults,
            DateTimeOffset.UtcNow);
    }
}
