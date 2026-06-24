using Ulbra.One.Accounting.Domain.Entities;

namespace Ulbra.One.Accounting.Application.Seeding;

/// <summary>Single entry point to materialize a chart-of-accounts template for a company.</summary>
public static class ChartTemplates
{
    public static IReadOnlyList<LedgerAccount> Build(Guid companyId, ChartTemplate template) => template switch
    {
        ChartTemplate.Demo => BrazilianChartTemplate.Build(companyId),
        ChartTemplate.RfbImunesIsentas =>
            ReferentialChartImporter.Build(companyId, ReferentialChartData.Load("referencial-rfb-imunes-isentas.json")),
        ChartTemplate.RfbPjLucroReal =>
            ReferentialChartImporter.Build(companyId, ReferentialChartData.Load("referencial-rfb-pj-lucro-real.json")),
        _ => throw new ArgumentOutOfRangeException(nameof(template), template, "Unknown chart template.")
    };
}
