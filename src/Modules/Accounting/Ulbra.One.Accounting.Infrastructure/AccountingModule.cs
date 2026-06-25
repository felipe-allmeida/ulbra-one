using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ulbra.One.Accounting.Infrastructure;

public static class AccountingModule
{
    public static IServiceCollection AddAccountingModule(this IServiceCollection services, IConfiguration configuration)
    {
        // RFB Referencial defaults:
        //   PayablesLiabilityCode: "2.01.01.03.01" = Fornecedores - Operações com Partes Não Relacionadas - No País – Circulante
        //   CashCode: "1.01.01.02.01" = Bancos Conta Movimento - No País
        var opts = new Ulbra.One.Accounting.Application.Posting.AccountingPostingOptions(
            configuration["Accounting:PayablesLiabilityCode"] ?? "2.01.01.03.01",
            configuration["Accounting:CashCode"] ?? "1.01.01.02.01");
        services.AddSingleton(opts);
        services.AddScoped<Ulbra.One.Accounting.Application.Posting.IPayablePostingService,
            Ulbra.One.Accounting.Application.Posting.PayablePostingService>();

        return services;
    }
}
