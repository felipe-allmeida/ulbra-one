using System.Globalization;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.AuditTrail;

/// <summary>
/// Synthesizes a payable's typed audit timeline from its explicit audit fields (hybrid source "C").
/// Two actions lack an entity timestamp — Payable.Cancel and Payment.Reverse — so the caller supplies
/// those times from the AuditLog (falling back to a sensible default when absent).
/// </summary>
public static class PayableAuditTrailBuilder
{
    private const string SystemActorName = "Sistema Ulbra.One";

    public static IReadOnlyList<AuditTrailEventResponse> Build(
        Payable payable,
        IReadOnlyDictionary<Guid, string> userNames,
        DateTimeOffset? cancelledAt,
        IReadOnlyDictionary<Guid, DateTimeOffset> paymentReversalTimes)
    {
        var events = new List<AuditTrailEventResponse>();

        // Created — actor stored as a user-id string.
        events.Add(Event("Created", payable.CreatedBy, payable.CreatedAt, userNames, new Dictionary<string, string>()));

        // Classified — no dedicated timestamp; the classification settled by the time the title left
        // the classification stage. Use UpdatedAt as the best available marker when the title is classified.
        if (payable.IsClassified)
        {
            events.Add(Event("Classified", payable.UpdatedBy, payable.UpdatedAt, userNames,
                new Dictionary<string, string>()));
        }

        if (payable.ApprovedAt is { } approvedAt)
        {
            events.Add(Event("Approved", payable.ApprovedBy, approvedAt, userNames, new Dictionary<string, string>()));
        }

        if (payable.RejectedAt is { } rejectedAt)
        {
            var meta = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(payable.RejectionReason)) meta["reason"] = payable.RejectionReason;
            events.Add(Event("Rejected", payable.RejectedBy, rejectedAt, userNames, meta));
        }

        if (payable.Status == PayableStatus.Cancelled)
        {
            var meta = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(payable.CancelReason)) meta["reason"] = payable.CancelReason;
            // Cancel has no entity timestamp; use the AuditLog-sourced time, else fall back to UpdatedAt.
            events.Add(Event("Cancelled", payable.UpdatedBy, cancelledAt ?? payable.UpdatedAt, userNames, meta));
        }

        foreach (var installment in payable.Installments)
        {
            foreach (var payment in installment.Payments)
            {
                var regMeta = new Dictionary<string, string>
                {
                    ["installment"] = installment.Sequence.ToString(CultureInfo.InvariantCulture),
                    ["amount"] = payment.Amount.ToString(CultureInfo.InvariantCulture),
                };
                events.Add(EventByGuid("PaymentRegistered", payment.CreatedById, payment.CreatedAt, userNames, regMeta));

                if (payment.IsReversed)
                {
                    var revMeta = new Dictionary<string, string>();
                    if (!string.IsNullOrEmpty(payment.ReversalReason)) revMeta["reason"] = payment.ReversalReason;
                    // Payment.Reverse has no timestamp; use the AuditLog-sourced time, else fall back to the payment's CreatedAt.
                    var when = paymentReversalTimes.TryGetValue(payment.Id, out var t) ? t : payment.CreatedAt;
                    events.Add(EventByGuid("PaymentReversed", payment.CreatedById, when, userNames, revMeta));
                }
            }
        }

        // AttachmentUploaded — reconstructed from current attachment state.
        // Note: AttachmentDeleted is intentionally omitted; deleted attachments are gone from current state.
        // A future iteration can source deleted-attachment events from the AuditLog if needed.
        foreach (var attachment in payable.Attachments)
        {
            var meta = new Dictionary<string, string> { ["fileName"] = attachment.FileName };
            events.Add(EventByGuid("AttachmentUploaded", attachment.UploadedById, attachment.CreatedAt, userNames, meta));
        }

        return events.OrderBy(e => e.Timestamp).ToList();
    }

    // Actor stored as a free string user-id (Payable lifecycle fields).
    private static AuditTrailEventResponse Event(
        string type, string? actorId, DateTimeOffset at,
        IReadOnlyDictionary<Guid, string> names, IReadOnlyDictionary<string, string> meta)
    {
        string actorName = SystemActorName;
        // A non-Guid actorId is the literal system marker ("system"); only real users are stored as Guids.
        if (!string.IsNullOrEmpty(actorId) && Guid.TryParse(actorId, out var g))
        {
            actorName = names.TryGetValue(g, out var n) ? n : actorId;
        }
        return new AuditTrailEventResponse(type, string.IsNullOrEmpty(actorId) ? null : actorId, actorName, at, meta);
    }

    // Actor stored as a Guid (Payment).
    private static AuditTrailEventResponse EventByGuid(
        string type, Guid actorId, DateTimeOffset at,
        IReadOnlyDictionary<Guid, string> names, IReadOnlyDictionary<string, string> meta)
    {
        var actorName = names.TryGetValue(actorId, out var n) ? n : SystemActorName;
        return new AuditTrailEventResponse(type, actorId.ToString(), actorName, at, meta);
    }
}
