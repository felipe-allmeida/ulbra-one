using Ulbra.One.Finance.Application.AuditTrail;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.UnitTests.Finance;

public class PayableAuditTrailBuilderTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 20, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_emits_created_then_classified_then_approved_in_chronological_order()
    {
        var actor = Guid.NewGuid();
        var payable = PayableTestFactory.Scheduled(actorId: actor.ToString(), createdAt: T0,
            approvedAt: T0.AddDays(2), accountingAccountId: Guid.NewGuid());
        var names = new Dictionary<Guid, string> { [actor] = "Maria Santos" };

        var events = PayableAuditTrailBuilder.Build(payable, names, cancelledAt: null,
            paymentReversalTimes: new Dictionary<Guid, DateTimeOffset>());

        Assert.Collection(events,
            e => Assert.Equal("Created", e.Type),
            e => Assert.Equal("Classified", e.Type),
            e => Assert.Equal("Approved", e.Type));
        Assert.True(events[0].Timestamp <= events[1].Timestamp && events[1].Timestamp <= events[2].Timestamp);
        Assert.Equal("Maria Santos", events[0].ActorName); // resolved from the Guid actor id
    }

    [Fact]
    public void Build_uses_injected_cancelledAt_when_payable_was_cancelled()
    {
        var payable = PayableTestFactory.Cancelled(createdAt: T0, reason: "duplicado");
        var cancelledAt = T0.AddDays(1);

        var events = PayableAuditTrailBuilder.Build(payable, new Dictionary<Guid, string>(),
            cancelledAt: cancelledAt, paymentReversalTimes: new Dictionary<Guid, DateTimeOffset>());

        var cancel = Assert.Single(events, e => e.Type == "Cancelled");
        Assert.Equal(cancelledAt, cancel.Timestamp);
        Assert.Equal("duplicado", cancel.Metadata["reason"]);
    }

    [Fact]
    public void Build_falls_back_to_System_actor_name_when_actor_is_empty()
    {
        var payable = PayableTestFactory.Received(actorId: "", createdAt: T0);

        var events = PayableAuditTrailBuilder.Build(payable, new Dictionary<Guid, string>(),
            cancelledAt: null, paymentReversalTimes: new Dictionary<Guid, DateTimeOffset>());

        Assert.Equal("Sistema", events[0].ActorName);
    }

    [Fact]
    public void Build_emits_attachment_uploaded_events()
    {
        var uploader = Guid.NewGuid();
        var payable = PayableTestFactory.ReceivedWithAttachment(actorId: Guid.NewGuid().ToString(), createdAt: T0,
            uploaderId: uploader, fileName: "nf.xml", uploadedAt: T0.AddHours(1));
        var names = new Dictionary<Guid, string> { [uploader] = "Carlos" };

        var events = PayableAuditTrailBuilder.Build(payable, names, null, new Dictionary<Guid, DateTimeOffset>());

        var att = Assert.Single(events, e => e.Type == "AttachmentUploaded");
        Assert.Equal("nf.xml", att.Metadata["fileName"]);
        Assert.Equal("Carlos", att.ActorName);
    }
}
