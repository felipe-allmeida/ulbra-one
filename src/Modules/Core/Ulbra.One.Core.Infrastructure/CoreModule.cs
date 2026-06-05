using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Infrastructure.Persistence;
using Ulbra.One.Core.Infrastructure.Database;

namespace Ulbra.One.Core.Infrastructure;

public static class CoreModule
{
    public static IServiceCollection AddCoreModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("Connection string 'Database' is not configured.");

        services.AddDbContext<CoreContext>(options =>
            options.UseNpgsql(connectionString)
                   .UseSnakeCaseNamingConvention());

        services.AddKeyedScoped<IUnitOfWork>("core",
            (sp, _) => new EfUnitOfWork<CoreContext>(sp.GetRequiredService<CoreContext>()));

        return services;
    }
}
