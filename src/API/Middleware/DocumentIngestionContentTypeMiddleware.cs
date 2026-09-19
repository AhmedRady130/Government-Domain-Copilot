namespace GovernmentDomainCopilot.API.Middleware;

/// <summary>
/// Keeps the text-ingestion endpoint explicitly JSON-only. File uploads are not
/// an ingestion format in this vertical slice, so they are rejected before a
/// multipart body (and any caller-supplied filename) is processed.
/// </summary>
public sealed class DocumentIngestionContentTypeMiddleware
{
    private const string DocumentIngestionPath = "/api/documents";
    private readonly RequestDelegate _next;

    public DocumentIngestionContentTypeMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        if (HttpMethods.IsPost(context.Request.Method) &&
            string.Equals(context.Request.Path, DocumentIngestionPath, StringComparison.OrdinalIgnoreCase) &&
            !IsJson(context.Request.ContentType))
        {
            context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            await context.Response.WriteAsJsonAsync(new { error = "UnsupportedDocumentMediaType" });
            return;
        }

        await _next(context);
    }

    private static bool IsJson(string? contentType) =>
        !string.IsNullOrWhiteSpace(contentType) &&
        contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase);
}
