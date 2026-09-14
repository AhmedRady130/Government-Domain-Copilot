namespace GovernmentDomainCopilot.Application.Observability.Abstractions;

/// <summary>
/// Service abstraction to derive estimated cost for LLM invocations based on configured model pricing.
/// Distinguishes missing pricing (null) from zero cost (0.0m).
/// </summary>
public interface ICostCalculator
{
    /// <summary>
    /// Calculates the estimated cost for a given model and token counts.
    /// Returns null if token counts are unavailable or model pricing is not configured.
    /// </summary>
    decimal? CalculateEstimatedCost(string modelName, int? promptTokens, int? completionTokens);
}
