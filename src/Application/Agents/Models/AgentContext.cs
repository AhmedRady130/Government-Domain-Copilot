namespace GovernmentDomainCopilot.Application.Agents.Models;

public sealed class AgentContext
{
    public Guid TenantId { get; }
    public string RunId { get; }
    public string CorrelationId { get; }
    public string UserQuery { get; }
    public Dictionary<string, object?> State { get; }

    public AgentContext(
        Guid tenantId,
        string runId,
        string correlationId,
        string userQuery,
        Dictionary<string, object?>? state = null)
    {
        TenantId = tenantId;
        RunId = runId;
        CorrelationId = correlationId;
        UserQuery = userQuery;
        State = state ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    }

    public T? GetState<T>(string key)
    {
        if (State.TryGetValue(key, out var val) && val is T typedVal)
        {
            return typedVal;
        }
        return default;
    }

    public void SetState<T>(string key, T value)
    {
        State[key] = value;
    }
}
