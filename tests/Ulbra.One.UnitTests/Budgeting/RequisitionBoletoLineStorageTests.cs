using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Domain;
using Xunit;

namespace Ulbra.One.UnitTests.Budgeting;

/// <summary>The requisition side stores the linha digitável canonically too — the duplicate check compares
/// Finance installments and requisition plans against each other, so both must store the same shape.</summary>
public class RequisitionBoletoLineStorageTests
{
    private const string Masked = "03399.12345 67890.123456 78901.234567 8 90120000010000";
    private const string Digits = "03399123456789012345678901234567890120000010000";

    [Fact]
    public void An_authored_installment_stores_the_line_as_digits()
    {
        var installment = new RequisitionInstallment(
            Guid.NewGuid(), 1, new DateOnly(2026, 9, 10), 100m, PaymentMethod.Boleto, Masked);

        Assert.Equal(Digits, installment.BoletoDigitableLine);
    }

    [Fact]
    public void A_recurrence_cycle_stores_the_line_as_digits()
    {
        var cycle = new RequisitionCycle(
            Guid.NewGuid(), 1, "2026-09", 100m, new DateOnly(2026, 9, 10), PaymentMethod.Boleto, Masked,
            Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Equal(Digits, cycle.BoletoDigitableLine);
    }

    [Fact]
    public void A_pix_installment_carrying_no_line_stores_null()
    {
        var installment = new RequisitionInstallment(
            Guid.NewGuid(), 1, new DateOnly(2026, 9, 10), 100m, PaymentMethod.Pix, "  ");

        Assert.Null(installment.BoletoDigitableLine);
    }
}
