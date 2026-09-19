namespace GovernmentDomainCopilot.Application.Observability;

using System.Diagnostics;
using Microsoft.Extensions.Logging;

/// <summary>Logs failure metadata without passing exception content to a log provider.</summary>
public static class SafeExceptionLoggingExtensions
{
    public static void LogSafeFailure(
        this ILogger logger,
        Exception exception,
        string errorCode,
        string operation,
        string? correlationId = null,
        TimeSpan? duration = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(exception);

        logger.LogWarning(
            "Operation {Operation} failed. ErrorCode={ErrorCode} ExceptionType={ExceptionType} CorrelationId={CorrelationId} DurationMs={DurationMs}",
            operation,
            errorCode,
            exception.GetType().Name,
            correlationId ?? Activity.Current?.Id ?? "unavailable",
            duration?.TotalMilliseconds ?? 0);
    }
}
