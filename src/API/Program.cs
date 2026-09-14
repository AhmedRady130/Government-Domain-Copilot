using GovernmentDomainCopilot.API.Endpoints;
using GovernmentDomainCopilot.API.Middleware;
using GovernmentDomainCopilot.Application;
using GovernmentDomainCopilot.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

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

app.MapGet("/ready", async (GovernmentDomainCopilot.Infrastructure.Persistence.GovernmentDomainCopilotDbContext dbContext, CancellationToken cancellationToken) =>
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
