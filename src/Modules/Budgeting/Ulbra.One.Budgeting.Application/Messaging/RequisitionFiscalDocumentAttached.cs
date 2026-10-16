namespace Ulbra.One.Budgeting.Application.Messaging;

/// <summary>Published (EF bus outbox) when the requester's Nota Fiscal is carried onto the payable and that
/// payable had a pending "Solicitar NF" request — the reverse of <see cref="RequisitionFiscalDocumentRequested"/>.
/// Consumed by Notifications to tell the Contábil user who requested it (<paramref name="RequestedById"/>) that
/// the NF arrived. Lives in the Budgeting namespace because that's what Notifications references; Finance (which
/// owns the payable) publishes it. <paramref name="PayableId"/> is the idempotency key (one notification per
/// payable); <paramref name="RequisitionId"/> is the deep-link target.</summary>
public sealed record RequisitionFiscalDocumentAttached(
    Guid CompanyId,
    Guid RequestedById,
    int? Code,
    Guid RequisitionId,
    Guid PayableId,
    DateTimeOffset OccurredAt);
