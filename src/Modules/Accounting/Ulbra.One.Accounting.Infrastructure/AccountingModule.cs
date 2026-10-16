using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Infrastructure.Persistence;

namespace Ulbra.One.Accounting.Infrastructure;

public static class AccountingModule
{
    public static IServiceCollection AddAccountingModule(this IServiceCollection services, IConfiguration configuration)
    {
        // AELBRA "Modelo de Planos" defaults (analytical leaves active in the seeded chart):
        //   PayablesLiabilityCode: "2.2.05.15.080" = Fornecedores a Pagar (ativa; movimento/saldo em 2026)
        //   CashCode: "1.2.05.15.015" = Bco Banrisul CC 060289920-4 (conta movimento)
        //
        // Withholding defaults — supplier retention accounts (2.2.05.30.* RETENÇÃO — 🔶 contábil-validate):
        //   Irrf    → "2.2.05.30.020"  IRRF Retenção
        //   Pis     → "2.2.05.30.040"  PIS/COFINS/CSLL Retenção
        //   Cofins  → "2.2.05.30.040"
        //   Csll    → "2.2.05.30.040"
        //   Inss    → "2.2.05.30.080"  INSS Retenção
        //   Iss     → "2.2.05.30.060"  ISS Retenção
        //   Other   → intentionally NOT mapped (→ PendingAccountingPosting)
        var defaultWithholdingCodes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Irrf"]   = "2.2.05.30.020", // 🔶 contábil-validate
            ["Pis"]    = "2.2.05.30.040", // 🔶 contábil-validate
            ["Cofins"] = "2.2.05.30.040", // 🔶 contábil-validate
            ["Csll"]   = "2.2.05.30.040", // 🔶 contábil-validate
            ["Inss"]   = "2.2.05.30.080", // 🔶 contábil-validate
            ["Iss"]    = "2.2.05.30.060", // 🔶 contábil-validate
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
            configuration["Accounting:PayablesLiabilityCode"] ?? "2.2.05.15.080",
            configuration["Accounting:CashCode"] ?? "1.2.05.15.015",
            withholdingCodes,
            configuration["Accounting:ApuracaoCode"] ?? "2.6.20.05.100",
            configuration["Accounting:RetainedEarningsCode"] ?? "2.6.20.05.020",
            // Opt-in and intentionally without a default: the analytical has to exist in the chart first, and
            // which one it is (and under which group) is Contabilidade's call. Unset → previous behaviour.
            configuration["Accounting:UnclassifiedExpenseCode"]);
        services.AddSingleton(opts);
        services.AddScoped<Ulbra.One.Accounting.Application.Posting.IPayablePostingService,
            Ulbra.One.Accounting.Application.Posting.PayablePostingService>();
        services.AddScoped<Ulbra.One.Accounting.Application.Posting.IYearEndClosingService,
            Ulbra.One.Accounting.Application.Posting.YearEndClosingService>();
        services.AddScoped<Ulbra.One.Accounting.Application.CostCenters.ICostCenterLookup,
            Ulbra.One.Accounting.Infrastructure.CostCenters.CostCenterLookup>();
        services.AddScoped<Ulbra.One.Accounting.Application.Units.IUnitLookup,
            Ulbra.One.Accounting.Infrastructure.Units.UnitLookup>();

        services.AddModelConfiguration(typeof(AccountingModule).Assembly);

        return services;
    }
}
