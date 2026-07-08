using Ulbra.One.Accounting.Application.Seeding;

namespace Ulbra.One.UnitTests.Accounting;

public class ChartTemplatesTests
{
    [Fact]
    public void Demo_builds_the_minimal_template()
    {
        var accounts = ChartTemplates.Build(ChartTemplate.Demo);
        Assert.Equal(BrazilianChartTemplate.Build().Count, accounts.Count);
    }

    [Fact]
    public void Imunes_isentas_builds_full_referencial()
    {
        var accounts = ChartTemplates.Build(ChartTemplate.RfbImunesIsentas);
        Assert.Equal(515, accounts.Count);
    }

    [Fact]
    public void Pj_lucro_real_builds_full_referencial()
    {
        var accounts = ChartTemplates.Build(ChartTemplate.RfbPjLucroReal);
        Assert.Equal(1104, accounts.Count);
    }
}
