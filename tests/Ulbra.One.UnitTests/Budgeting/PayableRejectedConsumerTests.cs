using Microsoft.Extensions.Time.Testing;
using Ulbra.One.Budgeting.Domain;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Budgeting.Infrastructure.Messaging;
using Ulbra.One.Common.Domain;
using Ulbra.One.Budgeting.Application.Messaging;
using Ulbra.One.Finance.Application.Messaging;
using Ulbra.One.UnitTests.Finance; // PmtStubUow, CapturingPublishEndpoint
using Xunit;

namespace Ulbra.One.UnitTests.Budgeting;

/// <summary>AEL-935: a rejected requisition Payable returns its requisition to the requester (Complement) or
/// controllership (Triage). Idempotent — no-op when the requisition is missing or already left the payment stage.
/// Driven directly via ProcessAsync with the in-memory stub uow (no broker).</summary>
public class PayableRejectedConsumerTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Due = new(2026, 7, 10);

    private static PayableRejectedConsumer Sut(CapturingPublishEndpoint publish, params object[] entities) =>
        new(new PmtStubUow(entities), publish, new FakeTimeProvider(Now));

    private static PayableRejected Rejected(Guid requisitionId, string target = "Requester") =>
        new(Guid.NewGuid(), Guid.NewGuid(), requisitionId, "NF divergente", Actor.ToString(), target);

    private static Requisition NewRequisition() =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 42, RequisitionType.Purchase, "Comprar cadeiras",
            100m, EstimateType.Full, "2026-07", Guid.NewGuid(), Due, null, null, null, null);

    private static Requisition InPaymentRequisition()
    {
        var r = NewRequisition();
        r.Submit(Actor, Now);
        r.ApproveTriage(Actor, Now);
        r.SubmitFinancialComplement(Actor, Now, Due, [(Due, 100m, PaymentMethod.Pix, null, null)]);
        r.Approve(Actor, Now);
        return r;
    }

    [Fact]
    public async Task Returns_to_requester_moves_to_complement_and_publishes_the_transition()
    {
        var r = InPaymentRequisition();
        var publish = new CapturingPublishEndpoint();

        await Sut(publish, r).ProcessAsync(Rejected(r.Id), CancellationToken.None);

        Assert.Equal(RequisitionStatus.Complement, r.Status);
        Assert.Equal("NF divergente", r.Transitions[^1].Reason);
        Assert.Single(publish.Messages.OfType<RequisitionTransitioned>());
    }

    [Fact]
    public async Task Returns_to_controllership_moves_to_triage()
    {
        var r = InPaymentRequisition();
        var publish = new CapturingPublishEndpoint();

        await Sut(publish, r).ProcessAsync(Rejected(r.Id, "Controllership"), CancellationToken.None);

        Assert.Equal(RequisitionStatus.Triage, r.Status);
    }

    [Fact]
    public async Task Unknown_target_defaults_to_requester()
    {
        var r = InPaymentRequisition();
        var publish = new CapturingPublishEndpoint();

        await Sut(publish, r).ProcessAsync(Rejected(r.Id, "bogus"), CancellationToken.None);

        Assert.Equal(RequisitionStatus.Complement, r.Status);
    }

    [Fact]
    public async Task Missing_requisition_is_a_noop()
    {
        var publish = new CapturingPublishEndpoint();

        await Sut(publish).ProcessAsync(Rejected(Guid.NewGuid()), CancellationToken.None);

        Assert.Empty(publish.Messages.OfType<RequisitionTransitioned>());
    }

    [Fact]
    public async Task Already_returned_requisition_is_a_noop()
    {
        // Redelivery after the return already happened: the requisition left the payment stage.
        var r = NewRequisition();
        r.Submit(Actor, Now);
        r.ReturnForComplement(Actor, Now); // now in Complement, not a payment stage
        var publish = new CapturingPublishEndpoint();

        await Sut(publish, r).ProcessAsync(Rejected(r.Id), CancellationToken.None);

        Assert.Equal(RequisitionStatus.Complement, r.Status);
        Assert.Empty(publish.Messages.OfType<RequisitionTransitioned>());
    }
}
