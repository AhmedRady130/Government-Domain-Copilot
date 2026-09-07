namespace GovernmentDomainCopilot.Application.Answering.Abstractions;

using GovernmentDomainCopilot.Application.Answering.Models;
using GovernmentDomainCopilot.Application.Retrieval.Models;

public interface IEvidenceSufficiencyPolicy
{
    EvidenceSufficiencyResult Evaluate(IReadOnlyList<RerankResultItem> candidates);
}
