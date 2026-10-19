using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Infrastructure.Persistence;
using Ulbra.One.Registry.Application.Lookups;
using Ulbra.One.Registry.Infrastructure.Lookups;

namespace Ulbra.One.Registry.Infrastructure;

public static class RegistryModule
{
    // BrasilAPI (Vercel) 403s requests without a User-Agent; HttpClient sends none by default. (BrasilAPI #446)
    private const string LookupUserAgent = "Ulbra.One/1.0 (registry-lookup)";

    public static IServiceCollection AddRegistryModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient<ICepLookup, ViaCepLookup>(c =>
        {
            c.BaseAddress = new Uri("https://viacep.com.br");
            c.Timeout = TimeSpan.FromSeconds(8);
            c.DefaultRequestHeaders.UserAgent.ParseAdd(LookupUserAgent);
        });
        services.AddHttpClient<ICnpjLookup, BrasilApiCnpjLookup>(c =>
        {
            c.BaseAddress = new Uri("https://brasilapi.com.br");
            c.Timeout = TimeSpan.FromSeconds(8);
            c.DefaultRequestHeaders.UserAgent.ParseAdd(LookupUserAgent);
        });

        services.AddModelConfiguration(typeof(RegistryModule).Assembly);

        return services;
    }
}
