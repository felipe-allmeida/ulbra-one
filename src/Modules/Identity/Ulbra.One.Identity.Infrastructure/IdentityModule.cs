using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Infrastructure.Authentication;
using Ulbra.One.Common.Infrastructure.Persistence;
using Ulbra.One.Identity.Infrastructure.Database;
using Ulbra.One.Identity.Infrastructure.Services;

namespace Ulbra.One.Identity.Infrastructure;

public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("Connection string 'Database' is not configured.");

        services.AddDbContext<IdentityContext>(options =>
            options.UseNpgsql(connectionString)
                   .UseSnakeCaseNamingConvention());

        services.AddKeyedScoped<IUnitOfWork>("identity", (sp, _) =>
            new EfUnitOfWork<IdentityContext>(sp.GetRequiredService<IdentityContext>()));

        services.Configure<GoogleAuthOptions>(configuration.GetSection(GoogleAuthOptions.SectionName));

        services.AddScoped<IGoogleTokenValidator, GoogleTokenValidator>();
        services.AddSingleton<JwtTokenService>();
        services.AddScoped<UserContext>();

        services.AddJwtAuthentication(configuration);

        return services;
    }
}
