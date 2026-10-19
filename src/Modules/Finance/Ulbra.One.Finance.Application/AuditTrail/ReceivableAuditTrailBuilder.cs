using System.Globalization;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.AuditTrail;

/// <summary>Synthesizes a receivable's typed audit timeline from its explicit audit fields.</summary>
public static class ReceivableAuditTrailBuilder
{
    private const string SystemActorName = "Sistema Ulbra.One";

    public static IReadOnlyList<AuditTrailEventResponse> Build(
        Receivable receivable, IReadOnlyDictionary<Guid, UserProfile> userProfiles)
    {
        var events = new List<AuditTrailEventResponse>
        {
            FromString("Created", receivable.CreatedBy, receivable.CreatedAt, userProfiles, new Dictionary<string, string>()),
        };

        if (receivable.Status == ReceivableStatus.Cancelled && receivable.CancelledAt is { } cancelledAt)
        {
            var meta = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(receivable.CancelReason)) meta["reason"] = receivable.CancelReason;
            events.Add(FromGuid("Cancelled", receivable.CancelledById!.Value, cancelledAt, userProfiles, meta));
        }

        if (receivable.Status == ReceivableStatus.WrittenOff)
        {
            var meta = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(receivable.WriteOffReason)) meta["reason"] = receivable.WriteOffReason;
            // WriteOff carries no timestamp/actor on the entity; use UpdatedAt as the marker.
            events.Add(FromString("WrittenOff", receivable.UpdatedBy, receivable.UpdatedAt, userProfiles, meta));
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
                events.Add(FromGuid("ReceiptRegistered", receipt.CreatedById, receipt.CreatedAt, userProfiles, regMeta));

                if (receipt.IsReversed && receipt.ReversedAt is { } reversedAt)
                {
                    var revMeta = new Dictionary<string, string>();
                    if (!string.IsNullOrEmpty(receipt.ReversalReason)) revMeta["reason"] = receipt.ReversalReason;
                    var actor = receipt.ReversedById ?? receipt.CreatedById;
                    events.Add(FromGuid("ReceiptReversed", actor, reversedAt, userProfiles, revMeta));
                }
            }
        }

        return events.OrderBy(e => e.Timestamp).ToList();
    }

    private static AuditTrailEventResponse FromString(
        string type, string? actorId, DateTimeOffset at,
        IReadOnlyDictionary<Guid, UserProfile> profiles, IReadOnlyDictionary<string, string> meta)
    {
        var actorName = SystemActorName;
        string? picture = null;
        // A non-Guid actorId is the literal system marker ("system"); only real users are stored as Guids.
        if (!string.IsNullOrEmpty(actorId) && Guid.TryParse(actorId, out var g))
        {
            if (profiles.TryGetValue(g, out var p)) (actorName, picture) = (p.Name, p.PictureUrl);
            else actorName = actorId;
        }
        return new AuditTrailEventResponse(
            type, string.IsNullOrEmpty(actorId) ? null : actorId, actorName, picture, at, meta);
    }

    private static AuditTrailEventResponse FromGuid(
        string type, Guid actorId, DateTimeOffset at,
        IReadOnlyDictionary<Guid, UserProfile> profiles, IReadOnlyDictionary<string, string> meta)
    {
        var found = profiles.TryGetValue(actorId, out var p);
        return new AuditTrailEventResponse(
            type, actorId.ToString(), found ? p!.Name : SystemActorName, found ? p!.PictureUrl : null, at, meta);
    }
}
