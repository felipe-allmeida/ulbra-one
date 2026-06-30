using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ulbra.One.Fiscal.Infrastructure;

public static class FiscalModule
{
    public static IServiceCollection AddFiscalModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Serviços do módulo Fiscal (calculadora de retenção, regras) entram aqui.
        return services;
    }
}
