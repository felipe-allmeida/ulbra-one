using System.Globalization;
using System.Text.Json;
using Ulbra.One.Common.Application.Auditing;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.AuditTrail;

/// <summary>
/// Synthesizes a payable's typed audit timeline from its explicit audit fields (hybrid source "C").
/// Two actions lack an entity timestamp — Payable.Cancel and Payment.Reverse — so the caller supplies
/// those times from the AuditLog (falling back to a sensible default when absent). Returns/reopens are read
/// from the durable audit history so they survive Reopen clearing RejectedAt (AEL-935).
/// </summary>
public static class PayableAuditTrailBuilder
{
    private const string SystemActorName = "Sistema Ulbra.One";

    public static IReadOnlyList<AuditTrailEventResponse> Build(
        Payable payable,
        IReadOnlyDictionary<Guid, string> userNames,
        DateTimeOffset? cancelledAt,
        IReadOnlyDictionary<Guid, DateTimeOffset> paymentReversalTimes,
        IReadOnlyList<AuditChangeRecord> history,
        PayableTrailContext? context = null)
    {
        var ctx = context ?? PayableTrailContext.Empty;
        var events = new List<AuditTrailEventResponse>();

        // Requisition-sourced (AEL-793): the title was born from an APPROVED requisition, so the timeline starts
        // there and references the requisition (friendly code) instead of a generic "Título criado". Manual titles
        // keep "Título criado".
        var isRequisitionSourced = payable.OriginRequisitionId is not null || payable.OriginRequisitionCode is not null;
        if (isRequisitionSourced)
        {
            var originMeta = new Dictionary<string, string>();
            if (payable.OriginRequisitionCode is { } reqCode)
                originMeta["requisitionCode"] = reqCode.ToString(CultureInfo.InvariantCulture);

            // The requisition's own opening — the timeline used to start at the approval, hiding how long the
            // request waited before it. Only present when the requisition could be read (same company, not purged).
            if (ctx.RequisitionCreatedAt is { } reqCreatedAt)
                events.Add(Event("RequisitionCreated", ctx.RequisitionCreatedBy, reqCreatedAt, userNames, originMeta));

            events.Add(Event("RequisitionApproved", payable.CreatedBy, payable.CreatedAt, userNames, originMeta));
        }
        else
        {
            events.Add(Event("Created", payable.CreatedBy, payable.CreatedAt, userNames, new Dictionary<string, string>()));
        }

        // Classified — no dedicated timestamp; the classification settled by the time the title left
        // the classification stage. Use UpdatedAt as the best available marker when the title is classified.
        if (payable.IsClassified)
        {
            // Which account it was classified to — without it the event says something happened but not what.
            var classMeta = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(ctx.ClassifiedAccountCode)) classMeta["accountCode"] = ctx.ClassifiedAccountCode!;
            if (!string.IsNullOrEmpty(ctx.ClassifiedAccountName)) classMeta["accountName"] = ctx.ClassifiedAccountName!;
            events.Add(Event("Classified", payable.UpdatedBy, payable.UpdatedAt, userNames, classMeta));
        }

        // Approve: for a requisition title the automatic system approval (ApprovedBy "system:req:…") is the mirror
        // of the requisition approval already shown above — skip it. A manual human approval is still recorded.
        if (payable.ApprovedAt is { } approvedAt
            && !(isRequisitionSourced && payable.ApprovedBy != null
                 && payable.ApprovedBy.StartsWith("system:req:", StringComparison.Ordinal)))
        {
            // "system" (not "system:req:…") is the cost-centre auto-approval: the title came in under the limit
            // and no human ever saw it. Saying so is the difference between "aprovado" and "ninguém aprovou".
            var apprMeta = new Dictionary<string, string>();
            if (payable.ApprovedBy == "system")
            {
                apprMeta["auto"] = "limit";
                if (ctx.AutoApprovalLimit is { } lim)
                    apprMeta["limit"] = lim.ToString(CultureInfo.InvariantCulture);
            }
            events.Add(Event("Approved", payable.ApprovedBy, approvedAt, userNames, apprMeta));
        }

        // Tributário released the title from análise tributária (validated retenções) → Scheduled.
        if (payable.TaxReviewedAt is { } taxReviewedAt)
        {
            events.Add(Event("TaxAnalysisReleased", payable.TaxReviewedBy, taxReviewedAt, userNames, new Dictionary<string, string>()));
        }

        // Returns/rejects + reopens come from the durable audit history (not current fields): Reopen clears
        // RejectedAt/RejectionReason on re-approval, so a synthesize-from-state approach would lose the whole
        // return once the title came back. A requisition-sourced title's reject IS a "devolução" (AEL-935).
        foreach (var change in history)
        {
            if (change.AffectedColumns.Contains("RejectedAt") && RejectedAtWasSet(change.NewValues))
            {
                var meta = new Dictionary<string, string>();
                var reason = StringProp(change.NewValues, "RejectionReason");
                if (!string.IsNullOrEmpty(reason)) meta["reason"] = reason;
                events.Add(Event(isRequisitionSourced ? "Returned" : "Rejected", change.ActorUserId, change.OccurredAt, userNames, meta));
            }
            else if (change.AffectedColumns.Contains("Status")
                && IntProp(change.NewValues, "Status") == (int)PayableStatus.Received
                && IntProp(change.OldValues, "Status") == (int)PayableStatus.Rejected)
            {
                // The returned title came back for reprocessing (requisition corrected + re-approved).
                events.Add(Event("Reopened", change.ActorUserId, change.OccurredAt, userNames, new Dictionary<string, string>()));
            }
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
                // Where the baixa came from: the CNAB return, the statement reconciliation, or someone by hand.
                // The Notes are written by those flows, so they are the only signal available here.
                if (!string.IsNullOrEmpty(payment.Notes))
                {
                    regMeta["source"] = payment.Notes.StartsWith("CNAB", StringComparison.OrdinalIgnoreCase) ? "cnab"
                        : payment.Notes.StartsWith("Conciliação", StringComparison.OrdinalIgnoreCase) ? "conciliation"
                        : "manual";
                }
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

        // RemittanceGenerated — the step between scheduling and the bank paying. Without it the timeline jumps
        // from "aprovado" to "pagamento registrado" and the operator who sent the file disappears from the record.
        foreach (var remittance in ctx.RemittanceList)
        {
            var remMeta = new Dictionary<string, string>
            {
                ["fileName"] = remittance.FileName,
                ["bankCode"] = remittance.BankCode,
                ["amount"] = remittance.Amount.ToString(CultureInfo.InvariantCulture),
            };
            events.Add(Event("RemittanceGenerated", remittance.GeneratedBy, remittance.GeneratedAt, userNames, remMeta));
        }

        // BankReconciled — the statement line that proves the money left the account. Closes the timeline: the
        // steps before it are what the system believes, this one is what the bank confirms.
        foreach (var conc in ctx.ConciliationList)
        {
            var concMeta = new Dictionary<string, string>
            {
                ["amount"] = conc.Amount.ToString(CultureInfo.InvariantCulture),
                ["statementDate"] = conc.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["mode"] = conc.Linked ? "linked" : "settled",
            };
            events.Add(Event("BankReconciled", null, conc.At, userNames, concMeta));
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

    // Audit values are property-name-keyed JSON with enums serialized as their numeric value.
    private static bool RejectedAtWasSet(string? newValues) =>
        Prop(newValues, "RejectedAt") is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined };

    private static string? StringProp(string? json, string name) =>
        Prop(json, name) is { ValueKind: JsonValueKind.String } el ? el.GetString() : null;

    private static int? IntProp(string? json, string name) =>
        Prop(json, name) is { ValueKind: JsonValueKind.Number } el && el.TryGetInt32(out var n) ? n : null;

    private static JsonElement? Prop(string? json, string name)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(name, out var el) ? el.Clone() : null;
        }
        catch (JsonException) { return null; }
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
