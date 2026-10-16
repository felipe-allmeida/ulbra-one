namespace Ulbra.One.Budgeting.Application.Messaging;

/// <summary>Published (EF bus outbox) after Finance's "Solicitar NF" is resolved to the requisition's requester —
/// consumed by Notifications to ping the requester personally. <paramref name="RequesterId"/> is the resolved
/// Requisition.CreatedBy; <paramref name="RequestId"/> is the idempotency key (one per click at the sink).</summary>
public sealed record RequisitionFiscalDocumentRequested(
    Guid RequestId,
    Guid RequisitionId,
    Guid CompanyId,
    int Code,
    Guid RequesterId,
    DateTimeOffset OccurredAt);
