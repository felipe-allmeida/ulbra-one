using Ulbra.One.Accounting.Domain.Entities;

namespace Ulbra.One.Accounting.Application.Seeding;

/// <summary>Single entry point to materialize a chart-of-accounts template (global, shared by all companies).</summary>
public static class ChartTemplates
{
    public static IReadOnlyList<LedgerAccount> Build(ChartTemplate template) => template switch
    {
        ChartTemplate.Demo => BrazilianChartTemplate.Build(),
        ChartTemplate.RfbImunesIsentas =>
            ReferentialChartImporter.Build(ReferentialChartData.Load("referencial-rfb-imunes-isentas.json")),
        ChartTemplate.RfbPjLucroReal =>
            ReferentialChartImporter.Build(ReferentialChartData.Load("referencial-rfb-pj-lucro-real.json")),
        _ => throw new ArgumentOutOfRangeException(nameof(template), template, "Unknown chart template.")
    };
}
