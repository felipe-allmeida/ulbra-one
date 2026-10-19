using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application.Auditing;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Infrastructure.Persistence;

namespace Ulbra.One.Audit.Infrastructure;

public static class AuditModule
{
    public static IServiceCollection AddAuditModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("Connection string 'Database' is not configured.");

        // Audit's own SaveChanges interceptor, attached to AppDbContext through the IInterceptor seam
        // so Ulbra.One.Persistence doesn't need to reference this module.
        services.AddScoped<AuditLogInterceptor>();
        services.AddScoped<IInterceptor>(sp => sp.GetRequiredService<AuditLogInterceptor>());

        services.AddDbContext<AuditContext>((sp, options) =>
            options.UseNpgsql(connectionString, npgsql =>
                   {
                       npgsql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorCodesToAdd: null);
                       npgsql.CommandTimeout(30);
                   })
                   .UseSnakeCaseNamingConvention());

        services.AddKeyedScoped<IUnitOfWork>("audit",
            (sp, _) => new EfUnitOfWork<AuditContext>(sp.GetRequiredService<AuditContext>()));

        services.AddScoped<IAuditReader, AuditReader>();

        services.AddModelConfiguration<AuditLogModelConfiguration>();

        return services;
    }
}
