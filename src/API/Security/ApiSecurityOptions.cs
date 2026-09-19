namespace GovernmentDomainCopilot.API.Security;

public sealed class ApiSecurityOptions
{
    public const string SectionName = "ApiSecurity";

    public int MaxQueryLength { get; set; } = 4_000;

    public long MaxRequestBodyBytes { get; set; } = 2_000_000;

    /// <summary>Browser origins permitted to call the API. An empty list denies cross-origin browser calls.</summary>
    public List<string> AllowedOrigins { get; set; } = [];

    public ApiRateLimitingOptions RateLimiting { get; set; } = new();

    public void Validate()
    {
        if (MaxQueryLength <= 0)
            throw new InvalidOperationException($"{SectionName}:MaxQueryLength must be greater than zero.");

        if (MaxRequestBodyBytes <= 0)
            throw new InvalidOperationException($"{SectionName}:MaxRequestBodyBytes must be greater than zero.");

        foreach (var origin in AllowedOrigins)
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("https" or "http") ||
                uri.AbsolutePath != "/" ||
                !string.IsNullOrEmpty(uri.Query) ||
                !string.IsNullOrEmpty(uri.Fragment))
            {
                throw new InvalidOperationException($"{SectionName}:AllowedOrigins entries must be absolute HTTP(S) origins without a path, query, or fragment.");
            }
        }

        RateLimiting.Validate();
    }
}

public sealed class ApiRateLimitingOptions
{
    public int GlobalPermitLimit { get; set; } = 120;

    public int AiWorkloadPermitLimit { get; set; } = 20;

    public int WindowSeconds { get; set; } = 60;

    public int RetryAfterFallbackSeconds { get; set; } = 60;

    public void Validate()
    {
        if (GlobalPermitLimit <= 0)
            throw new InvalidOperationException("ApiSecurity:RateLimiting:GlobalPermitLimit must be greater than zero.");

        if (AiWorkloadPermitLimit <= 0)
            throw new InvalidOperationException("ApiSecurity:RateLimiting:AiWorkloadPermitLimit must be greater than zero.");

        if (WindowSeconds <= 0)
            throw new InvalidOperationException("ApiSecurity:RateLimiting:WindowSeconds must be greater than zero.");

        if (RetryAfterFallbackSeconds <= 0)
            throw new InvalidOperationException("ApiSecurity:RateLimiting:RetryAfterFallbackSeconds must be greater than zero.");
    }
}
