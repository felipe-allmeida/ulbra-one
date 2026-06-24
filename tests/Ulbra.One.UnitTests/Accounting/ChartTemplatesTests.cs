using Ulbra.One.Accounting.Application.Seeding;

namespace Ulbra.One.UnitTests.Accounting;

public class ChartTemplatesTests
{
    private static readonly Guid Company = Guid.NewGuid();

    [Fact]
    public void Demo_builds_the_minimal_template()
    {
        var accounts = ChartTemplates.Build(Company, ChartTemplate.Demo);
        Assert.Equal(BrazilianChartTemplate.Build(Company).Count, accounts.Count);
    }

    [Fact]
    public void Imunes_isentas_builds_full_referencial()
    {
        var accounts = ChartTemplates.Build(Company, ChartTemplate.RfbImunesIsentas);
        Assert.Equal(515, accounts.Count);
    }

    [Fact]
    public void Pj_lucro_real_builds_full_referencial()
    {
        var accounts = ChartTemplates.Build(Company, ChartTemplate.RfbPjLucroReal);
        Assert.Equal(1104, accounts.Count);
    }
}
