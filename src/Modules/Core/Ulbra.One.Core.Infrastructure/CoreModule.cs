using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Infrastructure.Persistence;

namespace Ulbra.One.Core.Infrastructure;

public static class CoreModule
{
    public static IServiceCollection AddCoreModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddModelConfiguration(typeof(CoreModule).Assembly);

        return services;
    }
}
