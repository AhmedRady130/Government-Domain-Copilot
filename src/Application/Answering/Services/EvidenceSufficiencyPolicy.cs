namespace GovernmentDomainCopilot.Application.Answering.Services;

using GovernmentDomainCopilot.Application.Answering.Abstractions;
using GovernmentDomainCopilot.Application.Answering.Models;
using GovernmentDomainCopilot.Application.Retrieval.Models;

public sealed class EvidenceSufficiencyPolicy : IEvidenceSufficiencyPolicy
{
    public const double DefaultMinRerankScoreThreshold = 0.15;
    public const string DefaultRefusalReason = "Insufficient evidence in the available corpus to answer this question reliably.";

    private readonly double _minRerankScoreThreshold;

    public EvidenceSufficiencyPolicy(double minRerankScoreThreshold = DefaultMinRerankScoreThreshold)
    {
        _minRerankScoreThreshold = minRerankScoreThreshold;
    }

    public EvidenceSufficiencyResult Evaluate(IReadOnlyList<RerankResultItem> candidates)
    {
        if (candidates == null || candidates.Count == 0)
        {
            return new EvidenceSufficiencyResult(false, DefaultRefusalReason);
        }

        // Top candidate threshold check
        var topCandidate = candidates[0];
        if (topCandidate.RerankScore < _minRerankScoreThreshold)
        {
            return new EvidenceSufficiencyResult(false, DefaultRefusalReason);
        }

        return new EvidenceSufficiencyResult(true);
    }
}
