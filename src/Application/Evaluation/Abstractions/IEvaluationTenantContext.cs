namespace GovernmentDomainCopilot.Application.Evaluation.Abstractions;

using GovernmentDomainCopilot.Application.Abstractions;

public interface IEvaluationTenantContext : ITenantContext
{
    void SetCurrentTenantId(Guid tenantId);
}
