using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Finance.Application.Conciliation;
using Ulbra.One.Finance.Infrastructure.Conciliation;

namespace Ulbra.One.Finance.Infrastructure;

public static class FinanceModule
{
    public static IServiceCollection AddFinanceModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IBankStatementParser, OfxStatementParser>();

        return services;
    }
}
