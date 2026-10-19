using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Infrastructure.Persistence;
using Ulbra.One.Finance.Application.Withholdings;
using Ulbra.One.Fiscal.Application.Withholdings;

namespace Ulbra.One.Fiscal.Infrastructure;

public static class FiscalModule
{
    public static IServiceCollection AddFiscalModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Withholding calculator: implements Finance's IWithholdingCalculator port using the Fiscal rules + resolver.
        services.AddScoped<IWithholdingCalculator, WithholdingCalculator>();

        services.AddModelConfiguration(typeof(FiscalModule).Assembly);

        return services;
    }
}
