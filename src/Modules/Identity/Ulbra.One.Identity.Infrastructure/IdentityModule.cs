using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Infrastructure.Authentication;
using Ulbra.One.Identity.Application;
using Ulbra.One.Identity.Application.Services;
using Ulbra.One.Identity.Infrastructure.Services;

namespace Ulbra.One.Identity.Infrastructure;

public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<GoogleAuthOptions>(configuration.GetSection(GoogleAuthOptions.SectionName));

        services.AddScoped<IGoogleTokenValidator, GoogleTokenValidator>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddScoped<UserContext>();

        services.AddJwtAuthentication(configuration);

        return services;
    }
}
