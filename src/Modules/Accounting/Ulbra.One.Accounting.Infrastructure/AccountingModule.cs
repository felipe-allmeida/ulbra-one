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
        //
        // Withholding defaults (RfbImunesIsentas leaf codes — 🔶 contábil-validate before production):
        //   Irrf    → "2.01.01.09.01"  Tributos Federais a Recolher
        //   Pis     → "2.01.01.09.01"  (same grouping as IRRF)
        //   Cofins  → "2.01.01.09.01"
        //   Csll    → "2.01.01.09.01"
        //   Inss    → "2.01.01.01.02"  INSS a Recolher
        //   Iss     → "2.01.01.09.03"  Tributos Municipais a Recolher
        //   Other   → intentionally NOT mapped (→ PendingAccountingPosting)
        var defaultWithholdingCodes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Irrf"]   = "2.01.01.09.01", // 🔶 contábil-validate
            ["Pis"]    = "2.01.01.09.01", // 🔶 contábil-validate
            ["Cofins"] = "2.01.01.09.01", // 🔶 contábil-validate
            ["Csll"]   = "2.01.01.09.01", // 🔶 contábil-validate
            ["Inss"]   = "2.01.01.01.02", // 🔶 contábil-validate
            ["Iss"]    = "2.01.01.09.03", // 🔶 contábil-validate
        };

        // Allow each entry to be overridden via Accounting:WithholdingCode:<TaxType> in config.
        // Unknown keys present only in config are also picked up.
        var withholdingCodes = new Dictionary<string, string>(defaultWithholdingCodes, StringComparer.OrdinalIgnoreCase);
        var section = configuration.GetSection("Accounting:WithholdingCode");
        foreach (var child in section.GetChildren())
        {
            if (!string.IsNullOrWhiteSpace(child.Value))
                withholdingCodes[child.Key] = child.Value;
        }

        var opts = new Ulbra.One.Accounting.Application.Posting.AccountingPostingOptions(
            configuration["Accounting:PayablesLiabilityCode"] ?? "2.01.01.03.01",
            configuration["Accounting:CashCode"] ?? "1.01.01.02.01",
            withholdingCodes);
        services.AddSingleton(opts);
        services.AddScoped<Ulbra.One.Accounting.Application.Posting.IPayablePostingService,
            Ulbra.One.Accounting.Application.Posting.PayablePostingService>();

        return services;
    }
}
