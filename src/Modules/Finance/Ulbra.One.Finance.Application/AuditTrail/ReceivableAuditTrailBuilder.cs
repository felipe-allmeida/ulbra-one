using System.Globalization;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.AuditTrail;

/// <summary>Synthesizes a receivable's typed audit timeline from its explicit audit fields.</summary>
public static class ReceivableAuditTrailBuilder
{
    private const string SystemActorName = "Sistema Ulbra.One";

    public static IReadOnlyList<AuditTrailEventResponse> Build(
        Receivable receivable, IReadOnlyDictionary<Guid, string> userNames)
    {
        var events = new List<AuditTrailEventResponse>
        {
            FromString("Created", receivable.CreatedBy, receivable.CreatedAt, userNames, new Dictionary<string, string>()),
        };

        if (receivable.Status == ReceivableStatus.Cancelled && receivable.CancelledAt is { } cancelledAt)
        {
            var meta = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(receivable.CancelReason)) meta["reason"] = receivable.CancelReason;
            events.Add(FromGuid("Cancelled", receivable.CancelledById!.Value, cancelledAt, userNames, meta));
        }

        if (receivable.Status == ReceivableStatus.WrittenOff)
        {
            var meta = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(receivable.WriteOffReason)) meta["reason"] = receivable.WriteOffReason;
            // WriteOff carries no timestamp/actor on the entity; use UpdatedAt as the marker.
            events.Add(FromString("WrittenOff", receivable.UpdatedBy, receivable.UpdatedAt, userNames, meta));
        }

        foreach (var installment in receivable.Installments)
        {
            foreach (var receipt in installment.Receipts)
            {
                var regMeta = new Dictionary<string, string>
                {
                    ["installment"] = installment.Sequence.ToString(CultureInfo.InvariantCulture),
                    ["amount"] = receipt.Amount.ToString(CultureInfo.InvariantCulture),
                };
                events.Add(FromGuid("ReceiptRegistered", receipt.CreatedById, receipt.CreatedAt, userNames, regMeta));

                if (receipt.IsReversed && receipt.ReversedAt is { } reversedAt)
                {
                    var revMeta = new Dictionary<string, string>();
                    if (!string.IsNullOrEmpty(receipt.ReversalReason)) revMeta["reason"] = receipt.ReversalReason;
                    var actor = receipt.ReversedById ?? receipt.CreatedById;
                    events.Add(FromGuid("ReceiptReversed", actor, reversedAt, userNames, revMeta));
                }
            }
        }

        return events.OrderBy(e => e.Timestamp).ToList();
    }

    private static AuditTrailEventResponse FromString(
        string type, string? actorId, DateTimeOffset at,
        IReadOnlyDictionary<Guid, string> names, IReadOnlyDictionary<string, string> meta)
    {
        string actorName = SystemActorName;
        // A non-Guid actorId is the literal system marker ("system"); only real users are stored as Guids.
        if (!string.IsNullOrEmpty(actorId) && Guid.TryParse(actorId, out var g))
            actorName = names.TryGetValue(g, out var n) ? n : actorId;
        return new AuditTrailEventResponse(type, string.IsNullOrEmpty(actorId) ? null : actorId, actorName, at, meta);
    }

    private static AuditTrailEventResponse FromGuid(
        string type, Guid actorId, DateTimeOffset at,
        IReadOnlyDictionary<Guid, string> names, IReadOnlyDictionary<string, string> meta)
    {
        var actorName = names.TryGetValue(actorId, out var n) ? n : SystemActorName;
        return new AuditTrailEventResponse(type, actorId.ToString(), actorName, at, meta);
    }
}
