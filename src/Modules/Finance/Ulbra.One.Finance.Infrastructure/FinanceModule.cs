using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Infrastructure.Auditing;
using Ulbra.One.Common.Infrastructure.Persistence;
using Ulbra.One.Finance.Infrastructure.Database;

namespace Ulbra.One.Finance.Infrastructure;

public static class FinanceModule
{
    public static IServiceCollection AddFinanceModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("Connection string 'Database' is not configured.");

        services.AddScoped<AuditInterceptor>();

        services.AddDbContext<FinanceContext>((sp, options) =>
            options.UseNpgsql(connectionString)
                   .UseSnakeCaseNamingConvention()
                   .AddInterceptors(sp.GetRequiredService<AuditInterceptor>()));

        services.AddKeyedScoped<IUnitOfWork>("finance",
            (sp, _) => new EfUnitOfWork<FinanceContext>(sp.GetRequiredService<FinanceContext>()));

        return services;
    }
}
