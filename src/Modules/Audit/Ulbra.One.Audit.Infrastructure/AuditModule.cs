using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Infrastructure.Persistence;

namespace Ulbra.One.Audit.Infrastructure;

public static class AuditModule
{
    public static IServiceCollection AddAuditModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("Connection string 'Database' is not configured.");

        // Single registration: the SaveChangesInterceptor is shared by every business DbContext.
        // It buffers captured changes in SavingChanges, then writes them via the injected AuditContext
        // in SavedChanges — a separate write that happens right after the business transaction commits
        // (separate-write model, like kota). Trade-off: if the audit write fails after the business
        // commit, the business change is durable but the audit row is lost.
        services.AddScoped<AuditLogInterceptor>();

        services.AddDbContext<AuditContext>((sp, options) =>
            options.UseNpgsql(connectionString, npgsql =>
                   {
                       npgsql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorCodesToAdd: null);
                       npgsql.CommandTimeout(30);
                   })
                   .UseSnakeCaseNamingConvention());

        services.AddKeyedScoped<IUnitOfWork>("audit",
            (sp, _) => new EfUnitOfWork<AuditContext>(sp.GetRequiredService<AuditContext>()));

        return services;
    }
}
