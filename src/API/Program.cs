using GovernmentDomainCopilot.API.Endpoints;
using GovernmentDomainCopilot.API.Middleware;
using GovernmentDomainCopilot.Application;
using GovernmentDomainCopilot.Infrastructure;
using GovernmentDomainCopilot.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
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

app.UseAuthentication();
app.UseAuthorization();

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

public partial class Program { }
