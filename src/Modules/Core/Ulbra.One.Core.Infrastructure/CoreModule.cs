using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ulbra.One.Core.Infrastructure;

public static class CoreModule
{
    public static IServiceCollection AddCoreModule(this IServiceCollection services, IConfiguration configuration)
    {
        return services;
    }
}
