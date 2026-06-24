using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ulbra.One.Accounting.Infrastructure;

public static class AccountingModule
{
    public static IServiceCollection AddAccountingModule(this IServiceCollection services, IConfiguration configuration)
    {
        // No services to register in the ledger-core slice; endpoints/seed arrive in a later pass.
        return services;
    }
}
