using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ulbra.One.Fiscal.Infrastructure;

public static class FiscalModule
{
    public static IServiceCollection AddFiscalModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Fiscal module services (withholding calculator, rules) go here.
        return services;
    }
}
