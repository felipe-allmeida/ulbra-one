using Ulbra.One.Accounting.Application.Seeding;

namespace Ulbra.One.UnitTests.Accounting;

public class ReferentialChartDataTests
{
    [Fact]
    public void Loads_imunes_isentas_with_expected_count()
    {
        var rows = ReferentialChartData.Load("referencial-rfb-imunes-isentas.json");
        Assert.Equal(515, rows.Count);
        var ativo = rows.Single(r => r.Code == "1");
        Assert.Equal("ATIVO", ativo.Name);
        Assert.Equal("S", ativo.Tipo);
        Assert.Null(ativo.Parent);
        Assert.Equal("1", ativo.Natureza);
    }

    [Fact]
    public void Loads_pj_lucro_real_with_expected_count()
    {
        var rows = ReferentialChartData.Load("referencial-rfb-pj-lucro-real.json");
        Assert.Equal(1104, rows.Count);
        // PL nested under "2" but flagged natureza 3 (Equity)
        var pl = rows.Single(r => r.Code == "2.03");
        Assert.Equal("3", pl.Natureza);
        Assert.Equal("2", pl.Parent);
    }
}
