using Microsoft.Extensions.Logging.Abstractions;
using Ulbra.One.Budgeting.Application.Messaging;
using Ulbra.One.Finance.Application.Messaging;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Finance.Infrastructure.Messaging;

namespace Ulbra.One.UnitTests.Finance;

/// <summary>AEL-855: cancelling a requisition cancels its still-open Payable (and publishes PayableCancelled so
/// Accounting reverses the incurral). A paid/partially-paid payable is left for a human (logged, not thrown).
/// Driven directly via ProcessAsync with the in-memory stub uow (no broker).</summary>
public class RequisitionCancelledConsumerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private static RequisitionCancelledConsumer Sut(CapturingPublishEndpoint publish, params object[] entities) =>
        new(new PmtStubUow(entities), publish, NullLogger<RequisitionCancelledConsumer>.Instance);

    private static RequisitionTransitioned Cancelled(Guid requisitionId) =>
        new(Guid.NewGuid(), requisitionId, Guid.NewGuid(), 42, "InPayment", "Cancelled", "Cancel",
            Guid.NewGuid(), null, "erro de lançamento", Now);

    /// <summary>Requisition-origin Payable in the Scheduled state (no payments).</summary>
    private static Payable ScheduledOriginPayable(Guid requisitionId)
    {
        var p = new Payable(Guid.NewGuid(), PayableOrigin.Purchase, Guid.NewGuid(), "ACME Ltda",
            documentNumber: null, issueDate: new DateOnly(2026, 7, 1), description: "Requisição 42",
            costCenterId: Guid.NewGuid(), categoryId: null,
            Payable.GenerateInstallments(1000m, 1, new DateOnly(2026, 8, 10), 1));
        p.SetOrigin(requisitionId, Guid.NewGuid());
        p.SetType(PayableType.Service);
        p.Classify(Guid.NewGuid());
        p.Approve("system", Now);
        p.CompleteAccounting("system", Now, hasFiscalDocument: true); // → Scheduled
        return p;
    }

    private static Payment Pay(Payable p, decimal amount)
    {
        var inst = p.Installments[0];
        var payment = new Payment(inst.Id, p.Id, new DateOnly(2026, 8, 10), amount, Guid.NewGuid(),
            PaymentMethod.Ted, 0, 0, 0, null, Guid.NewGuid(), Now);
        inst.RegisterPayment(payment);
        p.RecomputeSettlement();
        return payment;
    }

    [Fact]
    public async Task Cancels_scheduled_origin_payable_and_publishes_once()
    {
        var reqId = Guid.NewGuid();
        var p = ScheduledOriginPayable(reqId);
        var publish = new CapturingPublishEndpoint();

        await Sut(publish, p).ProcessAsync(Cancelled(reqId), CancellationToken.None);

        Assert.Equal(PayableStatus.Cancelled, p.Status);
        var evt = Assert.Single(publish.Messages.OfType<PayableCancelled>());
        Assert.Equal(p.Id, evt.PayableId);
    }

    [Fact]
    public async Task Already_cancelled_payable_is_a_noop()
    {
        var reqId = Guid.NewGuid();
        var p = ScheduledOriginPayable(reqId);
        p.Cancel("already");
        var publish = new CapturingPublishEndpoint();

        await Sut(publish, p).ProcessAsync(Cancelled(reqId), CancellationToken.None);

        Assert.Empty(publish.Messages.OfType<PayableCancelled>());
    }

    [Fact]
    public async Task Paid_payable_is_left_for_a_human_no_publish_no_throw()
    {
        var reqId = Guid.NewGuid();
        var p = ScheduledOriginPayable(reqId);
        Pay(p, 1000m); // fully settles → Paid
        Assert.Equal(PayableStatus.Paid, p.Status);
        var publish = new CapturingPublishEndpoint();

        await Sut(publish, p).ProcessAsync(Cancelled(reqId), CancellationToken.None);

        Assert.Equal(PayableStatus.Paid, p.Status); // unchanged
        Assert.Empty(publish.Messages.OfType<PayableCancelled>());
    }

    [Fact]
    public async Task Partially_paid_payable_is_not_cancelled()
    {
        var reqId = Guid.NewGuid();
        var p = ScheduledOriginPayable(reqId);
        Pay(p, 400m); // partial → stays Scheduled with a non-reversed payment
        var publish = new CapturingPublishEndpoint();

        await Sut(publish, p).ProcessAsync(Cancelled(reqId), CancellationToken.None);

        Assert.NotEqual(PayableStatus.Cancelled, p.Status);
        Assert.Empty(publish.Messages.OfType<PayableCancelled>());
    }

    [Fact]
    public async Task All_payments_reversed_allows_cancellation()
    {
        var reqId = Guid.NewGuid();
        var p = ScheduledOriginPayable(reqId);
        var payment = Pay(p, 400m);
        payment.Reverse("estorno");
        p.RecomputeSettlement();
        var publish = new CapturingPublishEndpoint();

        await Sut(publish, p).ProcessAsync(Cancelled(reqId), CancellationToken.None);

        Assert.Equal(PayableStatus.Cancelled, p.Status);
        Assert.Single(publish.Messages.OfType<PayableCancelled>());
    }

    [Fact]
    public async Task Non_cancelled_transition_is_ignored()
    {
        var reqId = Guid.NewGuid();
        var p = ScheduledOriginPayable(reqId);
        var approved = new RequisitionTransitioned(Guid.NewGuid(), reqId, Guid.NewGuid(), 42,
            "Approval", "InPayment", "Approve", Guid.NewGuid(), null, null, Now);
        var publish = new CapturingPublishEndpoint();

        await Sut(publish, p).ProcessAsync(approved, CancellationToken.None);

        Assert.NotEqual(PayableStatus.Cancelled, p.Status);
        Assert.Empty(publish.Messages.OfType<PayableCancelled>());
    }

    [Fact]
    public async Task Missing_payable_is_a_noop()
    {
        var publish = new CapturingPublishEndpoint();

        // No payable seeded → nothing to cancel (the requisition never reached Finance, or the create-race guard
        // skipped it).
        await Sut(publish).ProcessAsync(Cancelled(Guid.NewGuid()), CancellationToken.None);

        Assert.Empty(publish.Messages.OfType<PayableCancelled>());
    }
}
