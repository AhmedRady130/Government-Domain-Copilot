namespace GovernmentDomainCopilot.Application.Evaluation.Services;

using GovernmentDomainCopilot.Application.Evaluation.Abstractions;

public sealed class EvaluationTenantContext : IEvaluationTenantContext
{
    private readonly AsyncLocal<Guid?> _currentTenantId = new();
    private readonly Guid _defaultTenantId;

    public EvaluationTenantContext(Guid? defaultTenantId = null)
    {
        _defaultTenantId = defaultTenantId ?? Guid.Parse("11111111-1111-1111-1111-111111111111");
    }

    public Guid GetTenantId()
    {
        return _currentTenantId.Value ?? _defaultTenantId;
    }

    public void SetCurrentTenantId(Guid tenantId)
    {
        _currentTenantId.Value = tenantId;
    }
}
