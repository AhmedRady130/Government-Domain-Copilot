using GovernmentDomainCopilot.API.Endpoints;
using GovernmentDomainCopilot.API.Middleware;
using GovernmentDomainCopilot.API.Security;
using GovernmentDomainCopilot.Application;
using GovernmentDomainCopilot.Infrastructure;
using GovernmentDomainCopilot.Infrastructure.Persistence;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

var apiSecurityOptions = builder.Configuration
    .GetSection(ApiSecurityOptions.SectionName)
    .Get<ApiSecurityOptions>() ?? new ApiSecurityOptions();
apiSecurityOptions.Validate();

builder.WebHost.ConfigureKestrel(options =>
    options.Limits.MaxRequestBodySize = apiSecurityOptions.MaxRequestBodyBytes);

builder.Services.AddEndpointsApiExplorer();
builder.Services.Configure<ApiSecurityOptions>(builder.Configuration.GetSection(ApiSecurityOptions.SectionName));
builder.Services.AddCors(options =>
{
    options.AddPolicy("ApiBrowserClients", policy =>
    {
        // No credentials are enabled. Production origins must be supplied through server configuration.
        policy.WithOrigins(apiSecurityOptions.AllowedOrigins.ToArray())
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        var retryAfter = apiSecurityOptions.RateLimiting.RetryAfterFallbackSeconds;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan metadata))
            retryAfter = Math.Max(1, (int)Math.Ceiling(metadata.TotalSeconds));

        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.Headers.RetryAfter = retryAfter.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await context.HttpContext.Response.WriteAsJsonAsync(new { error = "RateLimitExceeded" }, cancellationToken);
    };
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            GetPartitionKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = apiSecurityOptions.RateLimiting.GlobalPermitLimit,
                Window = TimeSpan.FromSeconds(apiSecurityOptions.RateLimiting.WindowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    options.AddPolicy(ApiRateLimitPolicies.AiWorkload, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            GetPartitionKey(context),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = apiSecurityOptions.RateLimiting.AiWorkloadPermitLimit,
                Window = TimeSpan.FromSeconds(apiSecurityOptions.RateLimiting.WindowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

// Compose invokes this mode in a one-shot container before starting the API.
// MigrateAsync uses EF's migration history, so it is safe to run repeatedly and
// never recreates or drops an existing database.
if (args.Contains("--migrate-only", StringComparer.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<GovernmentDomainCopilotDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
        .CreateLogger("DatabaseStartup");

    await DatabaseStartupInitializer.InitializeAsync(
        dbContext,
        app.Environment.IsDevelopment(),
        logger);

    return;
}

// Return clean, safe error responses without leaking internal stack traces or server implementation details
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exceptionHandlerPathFeature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerPathFeature>();
        var ex = exceptionHandlerPathFeature?.Error;

        if (ex is BadHttpRequestException)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Invalid request",
                details = "The request payload was invalid or could not be deserialized."
            });
            return;
        }

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            error = "Internal server error",
            details = "An unexpected error occurred processing your request."
        });
    });
});

// Correlation ID must be set before authentication so all downstream middleware
// (including the auth handler) can read the correlation context.
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<RequestBodySizeLimitMiddleware>();
app.UseMiddleware<DocumentIngestionContentTypeMiddleware>();

app.UseCors("ApiBrowserClients");
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }))
    .AllowAnonymous()
    .WithName("HealthCheck");

app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy" }))
    .AllowAnonymous()
    .WithName("LivenessCheck");

app.MapGet("/ready", async (GovernmentDomainCopilotDbContext dbContext, CancellationToken cancellationToken) =>
{
    try
    {
        bool canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
        if (canConnect)
        {
            return Results.Ok(new { status = "Ready" });
        }

        return Results.Json(new { status = "NotReady" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch
    {
        return Results.Json(new { status = "NotReady" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
})
.AllowAnonymous()
.WithName("ReadinessCheck");

app.MapDocumentEndpoints();
app.MapSearchEndpoints();
app.MapAnswerEndpoints();
app.MapOrchestrationEndpoints();
app.MapRunEndpoints();
app.MapSessionEndpoints();
app.MapTraceEndpoints();

app.Run();

static string GetPartitionKey(HttpContext context)
{
    var tenantId = context.User.FindFirst("tenant_id")?.Value;
    if (!string.IsNullOrWhiteSpace(tenantId))
        return $"tenant:{tenantId}";

    return $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
}

public partial class Program { }
