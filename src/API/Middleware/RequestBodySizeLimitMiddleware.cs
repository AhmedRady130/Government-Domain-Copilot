namespace GovernmentDomainCopilot.API.Middleware;

using GovernmentDomainCopilot.API.Security;
using Microsoft.Extensions.Options;

public sealed class RequestBodySizeLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ApiSecurityOptions _options;

    public RequestBodySizeLimitMiddleware(RequestDelegate next, IOptions<ApiSecurityOptions> options)
    {
        _next = next;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.ContentLength is > 0 and var contentLength && contentLength > _options.MaxRequestBodyBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "RequestBodyTooLarge"
            });
            return;
        }

        await _next(context);
    }
}
