using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Finance.Domain;
using Xunit;

namespace Ulbra.One.UnitTests.Finance;

public class InstallmentRemittanceTests
{
    private static Installment OpenInstallment() =>
        new(sequence: 1, dueDate: new DateOnly(2026, 6, 1), amount: 100m);

    [Fact]
    public void MarkInRemittance_from_open_moves_to_in_remittance()
    {
        var i = OpenInstallment();
        i.MarkInRemittance();
        Assert.Equal(InstallmentStatus.InRemittance, i.Status);
    }

    [Fact]
    public void MarkInRemittance_twice_throws()
    {
        var i = OpenInstallment();
        i.MarkInRemittance();
        Assert.Throws<InvalidOperationException>(() => i.MarkInRemittance());
    }

    [Fact]
    public void ReleaseFromRemittance_returns_to_open()
    {
        var i = OpenInstallment();
        i.MarkInRemittance();
        i.ReleaseFromRemittance();
        Assert.Equal(InstallmentStatus.Open, i.Status);
    }
}
