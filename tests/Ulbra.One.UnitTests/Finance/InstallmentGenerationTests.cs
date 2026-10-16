using Ulbra.One.Finance.Domain.Entities;
using Xunit;

namespace Ulbra.One.UnitTests.Finance;

public class InstallmentGenerationTests
{
    [Fact]
    public void Generate_single_installment_for_a_vista()
    {
        var rows = Payable.GenerateInstallments(1000m, 1, new DateOnly(2026, 7, 10), 1);
        Assert.Single(rows);
        Assert.Equal(1, rows[0].Sequence);
        Assert.Equal(1000m, rows[0].Amount);
        Assert.Equal(new DateOnly(2026, 7, 10), rows[0].DueDate);
    }

    [Fact]
    public void Generate_splits_equally_with_remainder_on_last_and_monthly_due_dates()
    {
        var rows = Payable.GenerateInstallments(100m, 3, new DateOnly(2026, 7, 10), 1);
        Assert.Equal(3, rows.Count);
        Assert.Equal(33.33m, rows[0].Amount);
        Assert.Equal(33.33m, rows[1].Amount);
        Assert.Equal(33.34m, rows[2].Amount);
        Assert.Equal(100m, rows[0].Amount + rows[1].Amount + rows[2].Amount);
        Assert.Equal(new DateOnly(2026, 7, 10), rows[0].DueDate);
        Assert.Equal(new DateOnly(2026, 8, 10), rows[1].DueDate);
        Assert.Equal(new DateOnly(2026, 9, 10), rows[2].DueDate);
    }
}
