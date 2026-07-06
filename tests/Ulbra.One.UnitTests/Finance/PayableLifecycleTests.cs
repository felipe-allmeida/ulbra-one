using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.UnitTests.Finance;

public class PayableLifecycleTests
{
    private static Payable NewPayable(decimal amount = 100m) =>
        new(Guid.NewGuid(), PayableOrigin.Manual, Guid.NewGuid(), "ACME", "NF-1", null,
            "svc", Guid.NewGuid(), null, new[] { new Installment(1, new DateOnly(2026, 1, 10), amount) });

    [Fact]
    public void New_payable_starts_received()
        => Assert.Equal(PayableStatus.Received, NewPayable().Status);

    [Fact]
    public void Approve_moves_to_scheduled()
    {
        var p = NewPayable();
        p.Approve("u", DateTimeOffset.UnixEpoch);
        Assert.Equal(PayableStatus.Scheduled, p.Status);
    }

    [Fact]
    public void AutoApproval_requires_classification_and_under_limit()
    {
        var unclassified = NewPayable(100m);
        unclassified.ApplyAutoApproval(500m, DateTimeOffset.UnixEpoch);
        Assert.Equal(PayableStatus.Received, unclassified.Status); // not classified → no auto-approve

        var classified = NewPayable(100m);
        classified.Classify(Guid.NewGuid(), Guid.NewGuid());
        classified.SetType(PayableType.Service); // type must be determined before auto-scheduling
        classified.ApplyAutoApproval(500m, DateTimeOffset.UnixEpoch);
        Assert.Equal(PayableStatus.Scheduled, classified.Status);

        var overLimit = NewPayable(1000m);
        overLimit.Classify(Guid.NewGuid(), Guid.NewGuid());
        overLimit.ApplyAutoApproval(500m, DateTimeOffset.UnixEpoch);
        Assert.NotEqual(PayableStatus.Scheduled, overLimit.Status);
    }

    [Fact]
    public void Classify_clears_persisted_suggestion()
    {
        var p = NewPayable();
        p.SetSuggestion(Guid.NewGuid(), Guid.NewGuid(), 0.6, "maybe");
        p.Classify(Guid.NewGuid(), Guid.NewGuid());
        Assert.Null(p.SuggestedAccountingAccountId);
        Assert.Null(p.ClassificationConfidence);
    }

    [Fact]
    public void RecomputeSettlement_paid_when_all_installments_paid()
    {
        var p = NewPayable(100m);
        p.Classify(Guid.NewGuid(), Guid.NewGuid());
        p.Approve("u", DateTimeOffset.UnixEpoch);
        var inst = p.Installments[0];
        inst.RegisterPayment(new Payment(inst.Id, p.Id, new DateOnly(2026, 1, 10), 100m,
            Guid.NewGuid(), PaymentMethod.Pix, 0, 0, 0, null, Guid.NewGuid(), default));
        p.RecomputeSettlement();
        Assert.Equal(PayableStatus.Paid, p.Status);
    }
}
