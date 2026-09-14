using GovernmentDomainCopilot.API.Endpoints;
using GovernmentDomainCopilot.Application;
using GovernmentDomainCopilot.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

app.MapDocumentEndpoints();
app.MapSearchEndpoints();
app.MapAnswerEndpoints();
app.MapOrchestrationEndpoints();
app.MapRunEndpoints();
app.MapSessionEndpoints();

app.Run();

public partial class Program { }
