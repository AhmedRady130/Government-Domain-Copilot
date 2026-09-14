using System;
using GovernmentDomainCopilot.Application.Observability.Abstractions;
using GovernmentDomainCopilot.Application.Observability.Models;
using Microsoft.Extensions.Options;

namespace GovernmentDomainCopilot.Application.Observability.Services;

/// <summary>
/// Derives estimated costs from configured model token pricing.
/// Correctly distinguishes unavailable pricing/tokens (null) from zero cost (0.0m).
/// </summary>
public sealed class ModelPricingCostCalculator : ICostCalculator
{
    private readonly ModelPricingOptions _options;

    public ModelPricingCostCalculator(IOptions<ModelPricingOptions> options)
    {
        _options = options?.Value ?? new ModelPricingOptions();
    }

    public decimal? CalculateEstimatedCost(string modelName, int? promptTokens, int? completionTokens)
    {
        if (string.IsNullOrWhiteSpace(modelName) || !promptTokens.HasValue || !completionTokens.HasValue)
        {
            return null;
        }

        // Normalize model name (e.g., strip "models/" prefix if present)
        var normalizedModel = modelName.StartsWith("models/", StringComparison.OrdinalIgnoreCase)
            ? modelName.Substring(7)
            : modelName;

        ModelTokenPricing? pricing = null;
        if (_options.Models.TryGetValue(normalizedModel, out var directMatch))
        {
            pricing = directMatch;
        }
        else if (_options.Models.TryGetValue(modelName, out var exactMatch))
        {
            pricing = exactMatch;
        }

        if (pricing == null)
        {
            // Missing pricing is represented as null, not fake zero
            return null;
        }

        var promptCost = (promptTokens.Value / 1_000_000m) * pricing.PromptCostPerMillion;
        var completionCost = (completionTokens.Value / 1_000_000m) * pricing.CompletionCostPerMillion;

        return Math.Round(promptCost + completionCost, 8);
    }
}
