using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.UnitTests.Finance;

/// <summary>Behaviour of the accounting stage (PendingAccounting) inserted between approval and scheduling.</summary>
public class PayableAccountingTests
{
    private static Payable NewPayable(decimal amount = 100m, PayableOrigin origin = PayableOrigin.Manual) =>
        new(Guid.NewGuid(), origin, Guid.NewGuid(), "ACME", "NF-1", null,
            "svc", Guid.NewGuid(), null, new[] { new Installment(1, new DateOnly(2026, 1, 10), amount) });

    private static Payable Approved(decimal amount = 100m, PayableOrigin origin = PayableOrigin.Manual)
    {
        var p = NewPayable(amount, origin);
        p.Classify(Guid.NewGuid());
        p.SetType(PayableType.Service);
        p.Approve("u", DateTimeOffset.UnixEpoch);
        return p;
    }

    [Fact]
    public void Approve_moves_to_pending_accounting()
    {
        var p = NewPayable();
        p.Approve("u", DateTimeOffset.UnixEpoch);
        Assert.Equal(PayableStatus.PendingAccounting, p.Status);
    }

    [Fact]
    public void AutoApproval_moves_to_pending_accounting()
    {
        var p = NewPayable(100m);
        p.Classify(Guid.NewGuid());
        p.SetType(PayableType.Service);
        p.ApplyAutoApproval(500m, DateTimeOffset.UnixEpoch);
        Assert.Equal(PayableStatus.PendingAccounting, p.Status);
    }

    [Fact]
    public void CompleteAccounting_with_fiscal_document_schedules_and_records_actor()
    {
        var p = Approved();
        var at = DateTimeOffset.UnixEpoch.AddDays(1);
        p.CompleteAccounting("accountant", at, hasFiscalDocument: true);
        Assert.Equal(PayableStatus.Scheduled, p.Status);
        Assert.Equal("accountant", p.AccountedBy);
        Assert.Equal(at, p.AccountedAt);
    }

    [Fact]
    public void CompleteAccounting_without_fiscal_document_throws_and_stays_pending_accounting()
    {
        var p = Approved();
        Assert.Throws<InvalidOperationException>(
            () => p.CompleteAccounting("accountant", DateTimeOffset.UnixEpoch, hasFiscalDocument: false));
        Assert.Equal(PayableStatus.PendingAccounting, p.Status);
    }

    [Fact]
    public void CompleteAccounting_advance_without_fiscal_document_schedules()
    {
        var p = Approved(origin: PayableOrigin.Advance);
        p.CompleteAccounting("accountant", DateTimeOffset.UnixEpoch, hasFiscalDocument: false);
        Assert.Equal(PayableStatus.Scheduled, p.Status);
        Assert.Equal("accountant", p.AccountedBy);
    }

    [Fact]
    public void CompleteAccounting_when_not_pending_accounting_throws()
    {
        var p = NewPayable(); // still Received
        Assert.Throws<InvalidOperationException>(
            () => p.CompleteAccounting("accountant", DateTimeOffset.UnixEpoch, hasFiscalDocument: true));
    }

    [Fact]
    public void SetWithholdings_allowed_in_pending_accounting()
    {
        var p = Approved(100m);
        Assert.Equal(PayableStatus.PendingAccounting, p.Status);

        p.SetWithholdings(new[] { (WithholdingTaxType.Irrf, 100m, (decimal?)0.015m, 1.5m) });

        Assert.Equal(1.5m, p.TotalWithheld);
        Assert.Equal(98.5m, p.NetTotal);
    }

    [Fact]
    public void SetWithholdings_blocked_after_scheduled()
    {
        var p = Approved(100m);
        p.CompleteAccounting("accountant", DateTimeOffset.UnixEpoch, hasFiscalDocument: true);
        Assert.Equal(PayableStatus.Scheduled, p.Status);

        Assert.Throws<InvalidOperationException>(
            () => p.SetWithholdings(new[] { (WithholdingTaxType.Irrf, 100m, (decimal?)0.015m, 1.5m) }));
    }
}
