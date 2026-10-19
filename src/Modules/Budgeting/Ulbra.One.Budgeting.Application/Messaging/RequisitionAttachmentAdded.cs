namespace Ulbra.One.Budgeting.Application.Messaging;

/// <summary>Published (EF bus outbox) when an attachment is added to a requisition that ALREADY generated a
/// Payable (Status InPayment/AwaitingFiscalDocument/Paid/Finalized). Finance's consumer copies the file onto the
/// existing Payable so a late Nota Fiscal clears the "NF pendente" pendency — the self-service loop where the
/// requester supplies the NF after approval. Carries the source blob key (as
/// <see cref="RequisitionApprovedForPayment"/> does) so Finance copies the bytes into its own key. Pre-approval
/// uploads ride <see cref="RequisitionApprovedForPayment"/> at approval instead, so this never double-copies.</summary>
public sealed record RequisitionAttachmentAdded(
    Guid RequisitionId,
    Guid CompanyId,
    RequisitionAttachmentRef Attachment,
    DateTimeOffset OccurredAt);
