using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

        services.AddDbContext<AppDbContext>((sp, options) =>
            options.UseNpgsql(connectionString, npgsql =>
                   {
                       npgsql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorCodesToAdd: null);
                       npgsql.CommandTimeout(30);
                       npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name);
                   })
                   // AppDbContext.NameRewriter mirrors this choice to recompute FK constraint names —
                   // changing the convention (or the culture) here requires changing it there too.
                   .UseSnakeCaseNamingConvention()
                   // Common interceptors run first (AuditInterceptor stamps CreatedBy/UpdatedBy),
                   // then whatever the modules registered — see the IInterceptor registrations in AddXModule.
                   .AddInterceptors([sp.GetRequiredService<AuditInterceptor>(), .. sp.GetServices<IInterceptor>()])
                   // First/FirstOrDefault with neither a filter nor an OrderBy returns an arbitrary row,
                   // and silently changes it between runs. Outside production that is a hard failure, so
                   // it surfaces in a test instead of as wrong data. Production keeps the warning: a query
                   // no test covers should not become a 500 in front of a user.
                   .ConfigureWarnings(w =>
                   {
                       if (!sp.GetRequiredService<IHostEnvironment>().IsProduction())
                           w.Throw(CoreEventId.FirstWithoutOrderByAndFilterWarning);
                   }));

        services.AddScoped<IUnitOfWork>(sp => new EfUnitOfWork<AppDbContext>(sp.GetRequiredService<AppDbContext>()));

        return services;
    }
}
