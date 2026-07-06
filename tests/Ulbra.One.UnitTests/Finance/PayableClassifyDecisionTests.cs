using Ulbra.One.Finance.Application.Classification;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Finance.Infrastructure.Messaging;

namespace Ulbra.One.UnitTests.Finance;

public class PayableClassifyDecisionTests
{
    private static Payable P(decimal amt = 100m) => new(Guid.NewGuid(), PayableOrigin.Manual, Guid.NewGuid(),
        "ACME", "NF", null, "svc", Guid.NewGuid(), null, new[] { new Installment(1, new DateOnly(2026,1,1), amt) });
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    [Fact]
    public void Confident_under_limit_auto_schedules()
    {
        var p = P(100m);
        p.SetType(PayableType.Service); // type must be determined before auto-scheduling
        PayableReceivedConsumer.Apply(p, new(Guid.NewGuid(), Guid.NewGuid(), 0.9, "r", "ai"), 500m, Now);
        Assert.True(p.IsClassified);
        Assert.Equal(PayableStatus.Scheduled, p.Status);
    }

    [Fact]
    public void Confident_over_limit_pending_approval()
    {
        var p = P(1000m);
        PayableReceivedConsumer.Apply(p, new(Guid.NewGuid(), Guid.NewGuid(), 0.9, "r", "ai"), 500m, Now);
        Assert.Equal(PayableStatus.PendingApproval, p.Status);
    }

    [Fact]
    public void Mid_band_parks_with_suggestion()
    {
        var p = P();
        var acc = Guid.NewGuid();
        PayableReceivedConsumer.Apply(p, new(acc, Guid.NewGuid(), 0.6, "maybe", "ai"), 500m, Now);
        Assert.Equal(PayableStatus.PendingClassification, p.Status);
        Assert.False(p.IsClassified);
        Assert.Equal(acc, p.SuggestedAccountingAccountId);
        Assert.Equal(0.6, p.ClassificationConfidence);
    }

    [Fact]
    public void Low_confidence_parks_blank()
    {
        var p = P();
        PayableReceivedConsumer.Apply(p, new(null, null, 0, "none", "none"), 500m, Now);
        Assert.Equal(PayableStatus.PendingClassification, p.Status);
        Assert.Null(p.SuggestedAccountingAccountId);
    }
}
