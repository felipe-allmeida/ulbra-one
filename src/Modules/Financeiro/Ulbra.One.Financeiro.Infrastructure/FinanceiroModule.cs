using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Financeiro.Infrastructure.Database;

namespace Ulbra.One.Financeiro.Infrastructure;

public static class FinanceiroModule
{
    public static IServiceCollection AddFinanceiroModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("Connection string 'Database' não configurada.");

        services.AddDbContext<FinanceiroContext>(options =>
            options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());

        return services;
    }
}
