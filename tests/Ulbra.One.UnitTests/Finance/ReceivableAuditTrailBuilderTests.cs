using Ulbra.One.Finance.Application.AuditTrail;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.UnitTests.Finance;

public class ReceivableAuditTrailBuilderTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 20, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_emits_created_then_cancelled_with_actor_and_reason()
    {
        var actor = Guid.NewGuid();
        var receivable = ReceivableTestFactory.Cancelled(
            createdActorId: "system", createdAt: T0, cancelledById: actor, cancelledAt: T0.AddDays(1), reason: "acordo");
        var names = new Dictionary<Guid, string> { [actor] = "Ana" };

        var events = ReceivableAuditTrailBuilder.Build(receivable, names);

        Assert.Collection(events,
            e => Assert.Equal("Created", e.Type),
            e =>
            {
                Assert.Equal("Cancelled", e.Type);
                Assert.Equal("Ana", e.ActorName);
                Assert.Equal("acordo", e.Metadata["reason"]);
                Assert.Equal(T0.AddDays(1), e.Timestamp);
            });
    }

    [Fact]
    public void Build_maps_system_actor_to_friendly_name_on_created_event()
    {
        // Unauthenticated/background contexts stamp the actor as the literal "system" (a non-Guid);
        // an empty actor is the other system case. Both must render as the friendly system name.
        var systemCreated = ReceivableTestFactory.Pending(createdActorId: "system", createdAt: T0);
        var emptyCreated = ReceivableTestFactory.Pending(createdActorId: "", createdAt: T0);

        var systemEvents = ReceivableAuditTrailBuilder.Build(systemCreated, new Dictionary<Guid, string>());
        var emptyEvents = ReceivableAuditTrailBuilder.Build(emptyCreated, new Dictionary<Guid, string>());

        Assert.Equal("Created", systemEvents[0].Type);
        Assert.Equal("Sistema Ulbra.One", systemEvents[0].ActorName);
        Assert.Equal("Sistema Ulbra.One", emptyEvents[0].ActorName);
    }

    [Fact]
    public void Build_emits_written_off_with_reason()
    {
        var receivable = ReceivableTestFactory.WrittenOff(
            createdActorId: "system", createdAt: T0, reason: "irrecuperável");
        // WrittenOff uses UpdatedAt as timestamp; factory calls MarkUpdated(T0.AddDays(1)) before WriteOff.
        var expectedTimestamp = T0.AddDays(1);

        var events = ReceivableAuditTrailBuilder.Build(receivable, new Dictionary<Guid, string>());

        Assert.Collection(events,
            e => Assert.Equal("Created", e.Type),
            e =>
            {
                Assert.Equal("WrittenOff", e.Type);
                Assert.Equal("irrecuperável", e.Metadata["reason"]);
                Assert.Equal(expectedTimestamp, e.Timestamp);
            });
    }

    [Fact]
    public void Build_emits_receipt_registered_with_installment_and_amount()
    {
        var receiptActor = Guid.NewGuid();
        var receivable = ReceivableTestFactory.WithReceipt(
            createdActorId: "system", createdAt: T0,
            receiptCreatedById: receiptActor, receiptCreatedAt: T0.AddDays(1), receiptAmount: 100m);
        var names = new Dictionary<Guid, string> { [receiptActor] = "Bruno" };

        var events = ReceivableAuditTrailBuilder.Build(receivable, names);

        Assert.Collection(events,
            e => Assert.Equal("Created", e.Type),
            e =>
            {
                Assert.Equal("ReceiptRegistered", e.Type);
                Assert.Equal("Bruno", e.ActorName);
                Assert.Equal("1", e.Metadata["installment"]);
                Assert.Equal("100", e.Metadata["amount"]);
                Assert.Equal(T0.AddDays(1), e.Timestamp);
            });
    }

    [Fact]
    public void Build_emits_receipt_reversed_with_actor_and_reason_and_ascending_order()
    {
        var receiptActor = Guid.NewGuid();
        var reversalActor = Guid.NewGuid();
        var receivable = ReceivableTestFactory.WithReversedReceipt(
            createdActorId: "system", createdAt: T0,
            receiptCreatedById: receiptActor, receiptCreatedAt: T0.AddDays(1),
            reversedById: reversalActor, reversedAt: T0.AddDays(2), reversalReason: "erro de lançamento",
            receiptAmount: 100m);
        var names = new Dictionary<Guid, string>
        {
            [receiptActor] = "Bruno",
            [reversalActor] = "Carla",
        };

        var events = ReceivableAuditTrailBuilder.Build(receivable, names);

        Assert.Collection(events,
            e => Assert.Equal("Created", e.Type),
            e => Assert.Equal("ReceiptRegistered", e.Type),
            e =>
            {
                Assert.Equal("ReceiptReversed", e.Type);
                Assert.Equal("Carla", e.ActorName);
                Assert.Equal("erro de lançamento", e.Metadata["reason"]);
                Assert.Equal(T0.AddDays(2), e.Timestamp);
            });
        // Assert ascending timestamp order
        Assert.True(events[0].Timestamp <= events[1].Timestamp);
        Assert.True(events[1].Timestamp <= events[2].Timestamp);
    }
}
