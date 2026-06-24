using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Finance.Application.Classification;
using Ulbra.One.Finance.Application.Conciliation;
using Ulbra.One.Finance.Application.FiscalDocuments;
using Ulbra.One.Finance.Application.Payments;
using Ulbra.One.Finance.Infrastructure.Conciliation;
using Ulbra.One.Finance.Infrastructure.FiscalDocuments;

namespace Ulbra.One.Finance.Infrastructure;

public static class FinanceModule
{
    public static IServiceCollection AddFinanceModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IPayableClassifier, PayableClassifier>();
        services.AddScoped<IPayablePaymentService, PayablePaymentService>();
        services.AddScoped<IBankStatementParser, OfxStatementParser>();

        // Fiscal-document reading: XML parsers (detected by namespace) + AI extractor for PDF/image.
        services.AddScoped<IFiscalXmlParser, NfeM55XmlParser>();
        services.AddScoped<IFiscalXmlParser, NfseAbrasfXmlParser>();
        services.AddScoped<AiFiscalExtractor>();
        services.AddScoped<IFiscalDocumentReader, FiscalDocumentReader>();

        return services;
    }
}
