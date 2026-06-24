using Ulbra.One.Accounting.Domain;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Accounting.Application.Seeding;

namespace Ulbra.One.UnitTests.Accounting;

public class ReferentialChartIntegrityTests
{
    private static readonly Guid Company = Guid.NewGuid();

    [Theory]
    [InlineData(ChartTemplate.RfbImunesIsentas, 515)]
    [InlineData(ChartTemplate.RfbPjLucroReal, 1104)]
    public void Real_template_is_internally_consistent(ChartTemplate template, int expectedCount)
    {
        var accounts = ChartTemplates.Build(Company, template);
        var byId = accounts.ToDictionary(a => a.Id);

        Assert.Equal(expectedCount, accounts.Count);
        // unique codes
        Assert.Equal(accounts.Count, accounts.Select(a => a.Code).Distinct().Count());
        // all five groups represented
        foreach (var g in new[] { AccountGroup.Asset, AccountGroup.Liability, AccountGroup.Equity, AccountGroup.Revenue, AccountGroup.Expense })
            Assert.Contains(accounts, a => a.AccountGroup == g);
        // every wired parent is a same-group synthetic account (invariant the importer guarantees)
        foreach (var a in accounts.Where(a => a.ParentId is not null))
        {
            var parent = byId[a.ParentId!.Value];
            Assert.Equal(LedgerAccountKind.Synthetic, parent.Kind);
            Assert.Equal(a.AccountGroup, parent.AccountGroup);
        }
    }

    [Fact]
    public void Imunes_isentas_pl_is_equity_root()
    {
        var accounts = ChartTemplates.Build(Company, ChartTemplate.RfbImunesIsentas);
        var pl = accounts.Single(a => a.Code == "2.05");
        Assert.Equal(AccountGroup.Equity, pl.AccountGroup);
        Assert.Null(pl.ParentId); // parent "2" is Liability → detached to root
    }
}
