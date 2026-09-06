using GovernmentDomainCopilot.Application.Documents;
using Microsoft.Extensions.DependencyInjection;

namespace GovernmentDomainCopilot.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IIngestDocumentUseCase, IngestDocumentUseCase>();
        return services;
    }
}
