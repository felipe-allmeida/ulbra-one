namespace Ulbra.One.Finance.Application.Messaging;

/// <summary>Published (EF bus outbox) when the Contábil clicks "Solicitar Nota Fiscal" on a requisition-sourced
/// payable that is still NF-pending. Finance can't read Budgeting entities, so it carries only what the Payable
/// knows; the Budgeting consumer resolves the requester (Requisition.CreatedBy) and re-publishes a
/// notification-ready event. <paramref name="RequestId"/> is the idempotency key (one per click).</summary>
public sealed record PayableFiscalDocumentRequested(
    Guid RequestId,
    Guid PayableId,
    Guid CompanyId,
    Guid RequisitionId,
    int? Code,
    DateTimeOffset OccurredAt);
