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

        // CNAB240 remittance file engine (Santander 033). Pure — no I/O.
        services.AddSingleton<SantanderCnabProfile>();
        services.AddSingleton<ICnabBankProfile>(sp => sp.GetRequiredService<SantanderCnabProfile>());
        services.AddSingleton<IReturnOutcomeResolver>(sp => sp.GetRequiredService<SantanderCnabProfile>());
        services.AddSingleton<IRemittanceFileGenerator, Cnab240RemittanceWriter>();
        services.AddSingleton<ICnabReturnParser, Cnab240ReturnParser>();

        // Remittance generation + return conciliation use-cases.
        services.AddScoped<IRemittanceGenerationService, RemittanceGenerationService>();
        services.AddScoped<IRemittanceReturnService, RemittanceReturnService>();
        services.AddScoped<IRemittanceSweepService, RemittanceSweepService>();

        // Development-only: synthetic return file simulator.
        services.AddScoped<IRemittanceReturnSimulator, CnabReturnSimulator>();

        // Fiscal-document reading: XML parsers (detected by namespace) + AI extractor for PDF/image.
        services.AddScoped<IFiscalXmlParser, NfeM55XmlParser>();
        services.AddScoped<IFiscalXmlParser, NfseAbrasfXmlParser>();
        services.AddScoped<IFiscalXmlParser, NfseNacionalXmlParser>();
        services.AddScoped<AiFiscalExtractor>();
        services.AddScoped<IFiscalDocumentReader, FiscalDocumentReader>();

        var sweepEnabled = configuration.GetValue("RemittanceSweep:Enabled", false);
        var sweepInterval = configuration.GetValue("RemittanceSweep:Interval", TimeSpan.FromHours(24));
        services.AddHostedService(sp => new RemittanceSweepJob(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<RemittanceSweepJob>>(),
            sweepEnabled, sweepInterval));

        return services;
    }
}
