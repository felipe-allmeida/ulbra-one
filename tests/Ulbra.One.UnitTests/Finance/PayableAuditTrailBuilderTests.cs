using Ulbra.One.Common.Application.Auditing;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Finance.Application.AuditTrail;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.UnitTests.Finance;

public class PayableAuditTrailBuilderTests
{
    private static readonly DateTimeOffset T0 = new(2026, 6, 20, 10, 0, 0, TimeSpan.Zero);
    private static readonly IReadOnlyList<AuditChangeRecord> NoHistory = [];

    [Fact]
    public void Build_emits_created_then_classified_then_approved_in_chronological_order()
    {
        var actor = Guid.NewGuid();
        var payable = PayableTestFactory.Scheduled(actorId: actor.ToString(), createdAt: T0,
            approvedAt: T0.AddDays(2), accountingAccountId: Guid.NewGuid());
        var names = new Dictionary<Guid, UserProfile> { [actor] = new("Maria Santos", null) };

        var events = PayableAuditTrailBuilder.Build(payable, names, cancelledAt: null,
            paymentReversalTimes: new Dictionary<Guid, DateTimeOffset>(), history: NoHistory);

        Assert.Collection(events,
            e => Assert.Equal("Created", e.Type),
            e => Assert.Equal("Classified", e.Type),
            e => Assert.Equal("Approved", e.Type));
        Assert.True(events[0].Timestamp <= events[1].Timestamp && events[1].Timestamp <= events[2].Timestamp);
        Assert.Equal("Maria Santos", events[0].ActorName); // resolved from the Guid actor id
    }

    [Fact]
    public void Build_requisition_title_starts_with_RequisitionApproved_and_skips_the_auto_system_approval()
    {
        var reqId = Guid.NewGuid();
        var payable = PayableTestFactory.Received(actorId: "system", createdAt: T0);
        payable.SetOrigin(reqId, Guid.NewGuid(), 70366); // requisition-sourced, friendly code #70366
        payable.Approve($"system:req:{reqId}", T0.AddMinutes(1)); // auto approval mirrors the requisition approval

        var events = PayableAuditTrailBuilder.Build(payable, new Dictionary<Guid, UserProfile>(), null,
            new Dictionary<Guid, DateTimeOffset>(), NoHistory);

        Assert.Equal("RequisitionApproved", events[0].Type); // timeline starts here...
        Assert.Equal("70366", events[0].Metadata["requisitionCode"]); // ...referencing the requisition
        Assert.DoesNotContain(events, e => e.Type == "Created"); // no generic "Título criado"
        Assert.DoesNotContain(events, e => e.Type == "Approved"); // the auto system:req approval isn't duplicated
    }

    [Fact]
    public void Build_uses_injected_cancelledAt_when_payable_was_cancelled()
    {
        var payable = PayableTestFactory.Cancelled(createdAt: T0, reason: "duplicado");
        var cancelledAt = T0.AddDays(1);

        var events = PayableAuditTrailBuilder.Build(payable, new Dictionary<Guid, UserProfile>(),
            cancelledAt: cancelledAt, paymentReversalTimes: new Dictionary<Guid, DateTimeOffset>(), history: NoHistory);

        var cancel = Assert.Single(events, e => e.Type == "Cancelled");
        Assert.Equal(cancelledAt, cancel.Timestamp);
        Assert.Equal("duplicado", cancel.Metadata["reason"]);
    }

    [Fact]
    public void Build_falls_back_to_System_actor_name_when_actor_is_empty()
    {
        var payable = PayableTestFactory.Received(actorId: "", createdAt: T0);

        var events = PayableAuditTrailBuilder.Build(payable, new Dictionary<Guid, UserProfile>(),
            cancelledAt: null, paymentReversalTimes: new Dictionary<Guid, DateTimeOffset>(), history: NoHistory);

        Assert.Equal("Sistema Ulbra.One", events[0].ActorName);
    }

    [Fact]
    public void Build_maps_literal_system_actor_to_friendly_name()
    {
        // Auto-approval and unauthenticated contexts stamp the actor as the literal "system"
        // (a non-Guid). It must render as the friendly system name, never leak the raw string.
        var payable = PayableTestFactory.Received(actorId: "system", createdAt: T0);

        var events = PayableAuditTrailBuilder.Build(payable, new Dictionary<Guid, UserProfile>(),
            cancelledAt: null, paymentReversalTimes: new Dictionary<Guid, DateTimeOffset>(), history: NoHistory);

        Assert.Equal("Created", events[0].Type);
        Assert.Equal("Sistema Ulbra.One", events[0].ActorName);
    }

    [Fact]
    public void Build_emits_attachment_uploaded_events()
    {
        var uploader = Guid.NewGuid();
        var payable = PayableTestFactory.ReceivedWithAttachment(actorId: Guid.NewGuid().ToString(), createdAt: T0,
            uploaderId: uploader, fileName: "nf.xml", uploadedAt: T0.AddHours(1));
        var names = new Dictionary<Guid, UserProfile> { [uploader] = new("Carlos", null) };

        var events = PayableAuditTrailBuilder.Build(payable, names, null, new Dictionary<Guid, DateTimeOffset>(), NoHistory);

        var att = Assert.Single(events, e => e.Type == "AttachmentUploaded");
        Assert.Equal("nf.xml", att.Metadata["fileName"]);
        Assert.Equal("Carlos", att.ActorName);
    }

    // AEL-935: returns/reopens come from the durable audit history so they survive Reopen clearing the fields.

    [Fact]
    public void Build_emits_durable_Returned_from_history_for_a_requisition_payable()
    {
        var actor = Guid.NewGuid();
        var payable = PayableTestFactory.Received(actorId: actor.ToString(), createdAt: T0);
        payable.SetOrigin(Guid.NewGuid(), Guid.NewGuid(), 12); // requisition-sourced → "Returned", not "Rejected"
        var names = new Dictionary<Guid, UserProfile> { [actor] = new("Maria", null) };
        var history = new List<AuditChangeRecord>
        {
            new(T0.AddDays(1), actor.ToString(), ["Status", "RejectedAt", "RejectionReason"],
                OldValues: """{"Status":2}""",
                NewValues: """{"Status":6,"RejectedAt":"2026-06-21T10:00:00+00:00","RejectionReason":"NF divergente"}"""),
        };

        var events = PayableAuditTrailBuilder.Build(payable, names, null, new Dictionary<Guid, DateTimeOffset>(), history);

        var ret = Assert.Single(events, e => e.Type == "Returned");
        Assert.Equal("NF divergente", ret.Metadata["reason"]);
        Assert.Equal("Maria", ret.ActorName);
        Assert.DoesNotContain(events, e => e.Type == "Rejected");
    }

    [Fact]
    public void Build_return_then_reopen_survives_the_field_clear()
    {
        // The payable is Received again now (Reopen cleared RejectedAt) — the durable history still carries BOTH
        // the return and the reopen, so the round-trip stays complete on the timeline.
        var actor = Guid.NewGuid();
        var payable = PayableTestFactory.Received(actorId: actor.ToString(), createdAt: T0);
        payable.SetOrigin(Guid.NewGuid(), Guid.NewGuid(), 12);
        var history = new List<AuditChangeRecord>
        {
            new(T0.AddDays(1), actor.ToString(), ["Status", "RejectedAt", "RejectionReason"],
                OldValues: """{"Status":2}""",
                NewValues: """{"Status":6,"RejectedAt":"2026-06-21T10:00:00+00:00","RejectionReason":"corrigir valor"}"""),
            new(T0.AddDays(2), "", ["Status", "RejectedAt", "RejectionReason"],
                OldValues: """{"Status":6}""",
                NewValues: """{"Status":0,"RejectedAt":null,"RejectionReason":null}"""),
        };

        var events = PayableAuditTrailBuilder.Build(payable, new Dictionary<Guid, UserProfile>(), null,
            new Dictionary<Guid, DateTimeOffset>(), history);

        Assert.Contains(events, e => e.Type == "Returned" && e.Metadata.GetValueOrDefault("reason") == "corrigir valor");
        Assert.Contains(events, e => e.Type == "Reopened");
    }

    [Fact]
    public void Build_non_requisition_reject_from_history_is_labeled_Rejected()
    {
        var actor = Guid.NewGuid();
        var payable = PayableTestFactory.Received(actorId: actor.ToString(), createdAt: T0); // no origin → terminal reject
        var history = new List<AuditChangeRecord>
        {
            new(T0.AddDays(1), actor.ToString(), ["Status", "RejectedAt", "RejectionReason"],
                OldValues: """{"Status":2}""",
                NewValues: """{"Status":6,"RejectedAt":"2026-06-21T10:00:00+00:00","RejectionReason":"fora do orçamento"}"""),
        };

        var events = PayableAuditTrailBuilder.Build(payable, new Dictionary<Guid, UserProfile>(), null,
            new Dictionary<Guid, DateTimeOffset>(), history);

        Assert.Single(events, e => e.Type == "Rejected");
        Assert.DoesNotContain(events, e => e.Type == "Returned");
    }

    // ── Contexto extra: requisição, conta contábil, remessa e origem da baixa ──

    [Fact]
    public void Requisition_opening_precedes_its_approval_on_the_timeline()
    {
        // The timeline used to start at the approval, hiding how long the request waited before it.
        var reqId = Guid.NewGuid();
        var payable = PayableTestFactory.Received(actorId: "system", createdAt: T0.AddDays(3));
        payable.SetOrigin(reqId, Guid.NewGuid(), 6);

        var ctx = new PayableTrailContext(RequisitionCreatedAt: T0, RequisitionCreatedBy: "system");
        var events = PayableAuditTrailBuilder.Build(payable, new Dictionary<Guid, UserProfile>(), null,
            new Dictionary<Guid, DateTimeOffset>(), NoHistory, ctx);

        Assert.Equal("RequisitionCreated", events[0].Type);
        Assert.Equal(T0, events[0].Timestamp);
        Assert.Equal("6", events[0].Metadata["requisitionCode"]);
        Assert.Equal("RequisitionApproved", events[1].Type);
    }

    [Fact]
    public void Classified_says_which_account_it_was_classified_to()
    {
        var payable = PayableTestFactory.Scheduled(actorId: "system", createdAt: T0,
            approvedAt: T0.AddDays(1), accountingAccountId: Guid.NewGuid());

        var ctx = new PayableTrailContext(
            ClassifiedAccountCode: "4.2.15.10.120", ClassifiedAccountName: "SUPORTE E LICENCA DE SOFTWARE");
        var events = PayableAuditTrailBuilder.Build(payable, new Dictionary<Guid, UserProfile>(), null,
            new Dictionary<Guid, DateTimeOffset>(), NoHistory, ctx);

        var classified = Assert.Single(events, e => e.Type == "Classified");
        Assert.Equal("4.2.15.10.120", classified.Metadata["accountCode"]);
        Assert.Equal("SUPORTE E LICENCA DE SOFTWARE", classified.Metadata["accountName"]);
    }

    [Fact]
    public void The_remittance_that_carried_the_title_to_the_bank_appears_on_the_timeline()
    {
        // Without it the timeline jumps from "aprovado" to "pagamento registrado" and the operator who sent
        // the file disappears from the record.
        var payable = PayableTestFactory.Scheduled(actorId: "system", createdAt: T0,
            approvedAt: T0.AddDays(1), accountingAccountId: Guid.NewGuid());
        var operatorId = Guid.NewGuid();

        var ctx = new PayableTrailContext(Remittances: [
            new RemittanceTrailEntry(T0.AddDays(2), operatorId.ToString(), "SANTANDER_REMESSA.txt", "033", 150m)]);
        var names = new Dictionary<Guid, UserProfile> { [operatorId] = new("Ângela Ferreira", null) };

        var events = PayableAuditTrailBuilder.Build(payable, names, null,
            new Dictionary<Guid, DateTimeOffset>(), NoHistory, ctx);

        var remittance = Assert.Single(events, e => e.Type == "RemittanceGenerated");
        Assert.Equal("SANTANDER_REMESSA.txt", remittance.Metadata["fileName"]);
        Assert.Equal("033", remittance.Metadata["bankCode"]);
        Assert.Equal("Ângela Ferreira", remittance.ActorName);
    }

    [Fact]
    public void An_automatic_approval_under_the_cost_centre_limit_says_so()
    {
        // "Aprovado" with no actor reads as if someone approved it; the difference matters in an audit.
        var payable = PayableTestFactory.Received(actorId: "system", createdAt: T0);
        payable.Approve("system", T0.AddMinutes(5));

        var ctx = new PayableTrailContext(AutoApprovalLimit: 5000m);
        var events = PayableAuditTrailBuilder.Build(payable, new Dictionary<Guid, UserProfile>(), null,
            new Dictionary<Guid, DateTimeOffset>(), NoHistory, ctx);

        var approved = Assert.Single(events, e => e.Type == "Approved");
        Assert.Equal("limit", approved.Metadata["auto"]);
        Assert.Equal("5000", approved.Metadata["limit"]);
    }
}
