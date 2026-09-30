using Eisai.Application.Interfaces.Persistence;
using Eisai.Infrastructure.Persistence.Connection;
using Eisai.Infrastructure.Persistence.Masters;
using Microsoft.Extensions.DependencyInjection;

namespace Eisai.Infrastructure.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddScoped<IDbConnectionFactory, SqlConnectionFactory>();
        services.AddScoped<IMasterCatalog, MasterCatalog>();

        return services;
    }
}
