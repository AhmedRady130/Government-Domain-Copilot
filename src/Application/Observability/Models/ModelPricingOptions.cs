using System;
using System.Collections.Generic;

namespace GovernmentDomainCopilot.Application.Observability.Models;

/// <summary>
/// Configuration options for model token pricing per million tokens.
/// Allows configurable cost estimation without hardcoding values in business logic.
/// </summary>
public sealed class ModelPricingOptions
{
    public const string SectionName = "ModelPricing";

    /// <summary>
    /// Dictionary of model name to pricing configuration.
    /// Key can be a model name or alias (case-insensitive).
    /// </summary>
    public Dictionary<string, ModelTokenPricing> Models { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gemini-2.5-flash"] = new ModelTokenPricing
        {
            PromptCostPerMillion = 0.075m,
            CompletionCostPerMillion = 0.30m
        },
        ["gemini-1.5-flash"] = new ModelTokenPricing
        {
            PromptCostPerMillion = 0.075m,
            CompletionCostPerMillion = 0.30m
        },
        ["gemini-1.5-pro"] = new ModelTokenPricing
        {
            PromptCostPerMillion = 1.25m,
            CompletionCostPerMillion = 5.00m
        }
    };
}

public sealed class ModelTokenPricing
{
    public decimal PromptCostPerMillion { get; set; }
    public decimal CompletionCostPerMillion { get; set; }
}
