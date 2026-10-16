using Ulbra.One.Finance.Domain.Withholdings;

namespace Ulbra.One.UnitTests.Finance;

public class WithholdingProrationTests
{
    // Single full payment settles the payable → withholds the whole total.
    [Fact]
    public void Full_single_payment_withholds_the_whole_total()
    {
        var amount = WithholdingProration.ForPayment(total: 46.50m, cumulativePrincipal: 955m, netTotal: 955m, alreadyWithheld: 0m, fullyPaid: true);
        Assert.Equal(46.50m, amount);
    }

    // Two equal parcelas: first (partial) gets half; second (settling) absorbs the exact residual.
    [Fact]
    public void Two_equal_parcelas_split_proportionally_with_residual_on_the_settling_payment()
    {
        // netTotal 1000; total CSRF 46.50; parcelas of 500 each.
        var first = WithholdingProration.ForPayment(total: 46.50m, cumulativePrincipal: 500m, netTotal: 1000m, alreadyWithheld: 0m, fullyPaid: false);
        Assert.Equal(23.25m, first);

        var second = WithholdingProration.ForPayment(total: 46.50m, cumulativePrincipal: 1000m, netTotal: 1000m, alreadyWithheld: first, fullyPaid: true);
        Assert.Equal(23.25m, second);
        Assert.Equal(46.50m, first + second);
    }

    // Rounding: three parcelas of 1/3 — running sum keeps Σ exact (settling payment absorbs the residual).
    [Fact]
    public void Three_parcelas_running_sum_totals_exactly()
    {
        const decimal total = 10.00m;
        const decimal net = 999m; // 333 each
        var p1 = WithholdingProration.ForPayment(total, 333m, net, 0m, false);
        var p2 = WithholdingProration.ForPayment(total, 666m, net, p1, false);
        var p3 = WithholdingProration.ForPayment(total, 999m, net, p1 + p2, true);
        Assert.Equal(total, p1 + p2 + p3);
        Assert.All(new[] { p1, p2, p3 }, a => Assert.True(a >= 0m));
    }

    [Theory]
    [InlineData(0, 100, 100, false)]   // nothing to withhold
    [InlineData(46.50, 100, 0, false)] // guard: no net
    public void Returns_zero_on_degenerate_inputs(double total, double cumulative, double net, bool fullyPaid) =>
        Assert.Equal(0m, WithholdingProration.ForPayment((decimal)total, (decimal)cumulative, (decimal)net, 0m, fullyPaid));

    // Never negative even if a prior partial rounded slightly high.
    [Fact]
    public void Never_returns_negative()
    {
        var amount = WithholdingProration.ForPayment(total: 10m, cumulativePrincipal: 100m, netTotal: 1000m, alreadyWithheld: 5m, fullyPaid: false);
        Assert.True(amount >= 0m);
    }
}
