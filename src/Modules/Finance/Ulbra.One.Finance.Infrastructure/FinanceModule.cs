using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Finance.Application.Conciliation;
using Ulbra.One.Finance.Application.FiscalDocuments;
using Ulbra.One.Finance.Infrastructure.Conciliation;
using Ulbra.One.Finance.Infrastructure.FiscalDocuments;

namespace Ulbra.One.Finance.Infrastructure;

public static class FinanceModule
{
    public static IServiceCollection AddFinanceModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IBankStatementParser, OfxStatementParser>();

        // Fiscal-document reading: XML parsers (detected by namespace) + service PDF mock.
        services.AddScoped<IFiscalXmlParser, NfeM55XmlParser>();
        services.AddScoped<IFiscalXmlParser, NfseAbrasfXmlParser>();
        services.AddScoped<PdfMockReader>();
        services.AddScoped<IFiscalDocumentReader, FiscalDocumentReader>();

        return services;
    }
}
