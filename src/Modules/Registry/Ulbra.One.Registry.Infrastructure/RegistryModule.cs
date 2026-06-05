using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Registry.Infrastructure.Database;
using Ulbra.One.Registry.Infrastructure.Lookups;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Infrastructure.Auditing;
using Ulbra.One.Common.Infrastructure.Persistence;

namespace Ulbra.One.Registry.Infrastructure;

public static class RegistryModule
{
    public static IServiceCollection AddRegistryModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("Connection string 'Database' is not configured.");

        services.AddScoped<AuditInterceptor>();

        services.AddDbContext<RegistryContext>((sp, options) =>
            options.UseNpgsql(connectionString)
                   .UseSnakeCaseNamingConvention()
                   .AddInterceptors(sp.GetRequiredService<AuditInterceptor>()));

        services.AddKeyedScoped<IUnitOfWork>("registry",
            (sp, _) => new EfUnitOfWork<RegistryContext>(sp.GetRequiredService<RegistryContext>()));

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
