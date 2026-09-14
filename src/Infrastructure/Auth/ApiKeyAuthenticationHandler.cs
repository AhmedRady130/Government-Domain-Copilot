using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GovernmentDomainCopilot.Infrastructure.Auth;

public sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-API-Key";
}

public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // 1. Check for API key in X-API-Key header or Authorization: Bearer <key>
        string? apiKey = null;
        if (Request.Headers.TryGetValue(ApiKeyAuthenticationOptions.HeaderName, out var headerVal))
        {
            apiKey = headerVal.ToString();
        }
        else if (Request.Headers.TryGetValue("Authorization", out var authHeaderVal))
        {
            var authHeader = authHeaderVal.ToString();
            if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                apiKey = authHeader.Substring(7).Trim();
            }
            else if (authHeader.StartsWith("ApiKey ", StringComparison.OrdinalIgnoreCase))
            {
                apiKey = authHeader.Substring(7).Trim();
            }
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            // No credentials provided -> NoResult allows challenge to return 401
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        // 2. Enforce environment boundary: SeedAuthIdentities are strictly for Development / Test
        var env = Context.RequestServices.GetService<Microsoft.Extensions.Hosting.IHostEnvironment>();
        if (env != null && !env.IsDevelopment())
        {
            Logger.LogWarning("Rejecting authentication attempt in Production environment: seeded identities are disabled outside Development/Test.");
            return Task.FromResult(AuthenticateResult.Fail("Production authentication requires a configured trusted identity source (e.g. OIDC/JWT). Seeded test identities are disabled outside Development/Test."));
        }

        var user = SeedAuthIdentities.FindByApiKey(apiKey);
        if (user == null)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid or unrecognized API key."));
        }

        // 3. Build authenticated ClaimsPrincipal containing UserId, TenantId, Role, ExternalId
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new("sub", user.UserId.ToString()),
            new("tenant_id", user.TenantId.ToString()),
            new(ClaimTypes.Name, user.DisplayName),
            new("external_id", user.ExternalId),
            new(ClaimTypes.Role, user.Role)
        };

        var identity = new ClaimsIdentity(claims, ApiKeyAuthenticationOptions.SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, ApiKeyAuthenticationOptions.SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
