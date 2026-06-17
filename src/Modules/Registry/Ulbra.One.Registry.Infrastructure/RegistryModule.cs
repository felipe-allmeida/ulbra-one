using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Registry.Application.Lookups;
using Ulbra.One.Registry.Infrastructure.Lookups;

namespace Ulbra.One.Registry.Infrastructure;

public static class RegistryModule
{
    public static IServiceCollection AddRegistryModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient<ICepLookup, ViaCepLookup>(c =>
        {
            c.BaseAddress = new Uri("https://viacep.com.br");
            c.Timeout = TimeSpan.FromSeconds(5);
        });
        services.AddHttpClient<ICnpjLookup, BrasilApiCnpjLookup>(c =>
        {
            c.BaseAddress = new Uri("https://brasilapi.com.br");
            c.Timeout = TimeSpan.FromSeconds(5);
        });

        return services;
    }
}
