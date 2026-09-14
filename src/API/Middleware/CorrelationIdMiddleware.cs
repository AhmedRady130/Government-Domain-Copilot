namespace GovernmentDomainCopilot.API.Middleware;

using GovernmentDomainCopilot.Application.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

/// <summary>
/// Reads X-Correlation-ID from the incoming request (or generates a new one),
/// sets it on the scoped ICorrelationContext, and echoes it back in the response header.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    public const string RequestHeaderName = "X-Correlation-ID";
    public const string ResponseHeaderName = "X-Correlation-ID";

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InvokeAsync(HttpContext context, ICorrelationContext correlationContext)
    {
        string correlationId;

        if (context.Request.Headers.TryGetValue(RequestHeaderName, out StringValues values)
            && !StringValues.IsNullOrEmpty(values)
            && !string.IsNullOrWhiteSpace(values[0]))
        {
            // Preserve caller-supplied correlation ID (max 100 chars to match DB column)
            correlationId = values[0]!.Trim();
            if (correlationId.Length > 100)
            {
                correlationId = correlationId[..100];
            }
        }
        else
        {
            correlationId = $"corr-{Guid.NewGuid():N}";
        }

        correlationContext.SetCorrelationId(correlationId);

        _logger.LogDebug("CorrelationId set to {CorrelationId} for {Method} {Path}",
            correlationId, context.Request.Method, context.Request.Path);

        // Echo the correlation ID back in the response so downstream callers can track it
        context.Response.OnStarting(() =>
        {
            if (!context.Response.Headers.ContainsKey(ResponseHeaderName))
            {
                context.Response.Headers.Append(ResponseHeaderName, correlationId);
            }
            return Task.CompletedTask;
        });

        await _next(context);
    }
}
