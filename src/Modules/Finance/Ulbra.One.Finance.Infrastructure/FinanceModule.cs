using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ulbra.One.Finance.Application.Classification;
using Ulbra.One.Finance.Application.Conciliation;
using Ulbra.One.Finance.Application.FiscalDocuments;
using Ulbra.One.Finance.Application.Payments;
using Ulbra.One.Finance.Application.Remittances;
using Ulbra.One.Finance.Infrastructure.Cnab;
using Ulbra.One.Finance.Infrastructure.Conciliation;
using Ulbra.One.Finance.Infrastructure.FiscalDocuments;
using Ulbra.One.Finance.Infrastructure.Jobs;

namespace Ulbra.One.Finance.Infrastructure;

public static class FinanceModule
{
    public static IServiceCollection AddFinanceModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IPayableClassifier, PayableClassifier>();
        services.AddScoped<IPayablePaymentService, PayablePaymentService>();
        services.AddScoped<IBankStatementParser, OfxStatementParser>();

        // CNAB240 remittance file engine (Santander 033). Pure — no I/O; Task 5 wires the use-case.
        services.AddSingleton<ICnabBankProfile, SantanderCnabProfile>();
        services.AddSingleton<IRemittanceFileGenerator, Cnab240RemittanceWriter>();

        // Fiscal-document reading: XML parsers (detected by namespace) + AI extractor for PDF/image.
        services.AddScoped<IFiscalXmlParser, NfeM55XmlParser>();
        services.AddScoped<IFiscalXmlParser, NfseAbrasfXmlParser>();
        services.AddScoped<AiFiscalExtractor>();
        services.AddScoped<IFiscalDocumentReader, FiscalDocumentReader>();

        var autoPayEnabled = configuration.GetValue("AutoPay:Enabled", true);
        var autoPayInterval = configuration.GetValue("AutoPay:Interval", TimeSpan.FromHours(24));
        services.AddHostedService(sp => new PayableAutoPayJob(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<PayableAutoPayJob>>(),
            autoPayEnabled, autoPayInterval));

        return services;
    }
}
