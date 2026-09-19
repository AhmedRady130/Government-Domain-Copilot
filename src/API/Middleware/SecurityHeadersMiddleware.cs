namespace GovernmentDomainCopilot.API.Middleware;

/// <summary>Adds baseline browser hardening headers to API responses.</summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.TryAdd("Content-Security-Policy", "default-src 'none'; base-uri 'none'; frame-ancestors 'none'; form-action 'none'");
            headers.TryAdd("X-Content-Type-Options", "nosniff");
            headers.TryAdd("X-Frame-Options", "DENY");
            headers.TryAdd("Referrer-Policy", "strict-origin-when-cross-origin");

            // Browsers honor HSTS only over HTTPS; emitting it over HTTP is ineffective and discouraged.
            if (context.Request.IsHttps)
                headers.TryAdd("Strict-Transport-Security", "max-age=31536000; includeSubDomains");

            return Task.CompletedTask;
        });

        await _next(context);
    }
}
