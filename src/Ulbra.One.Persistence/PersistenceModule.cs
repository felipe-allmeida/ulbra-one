using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Audit.Infrastructure;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Infrastructure.Auditing;
using Ulbra.One.Common.Infrastructure.Persistence;

namespace Ulbra.One.Persistence;

public static class PersistenceModule
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("Connection string 'Database' is not configured.");

        services.AddScoped<AuditInterceptor>();
        services.AddScoped<AuditLogInterceptor>();

        services.AddDbContext<AppDbContext>((sp, options) =>
            options.UseNpgsql(connectionString, npgsql =>
                   {
                       npgsql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorCodesToAdd: null);
                       npgsql.CommandTimeout(30);
                       npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name);
                   })
                   .UseSnakeCaseNamingConvention()
                   .AddInterceptors(
                       sp.GetRequiredService<AuditInterceptor>(),
                       sp.GetRequiredService<AuditLogInterceptor>()));

        services.AddScoped<IUnitOfWork>(sp => new EfUnitOfWork<AppDbContext>(sp.GetRequiredService<AppDbContext>()));

        return services;
    }
}
