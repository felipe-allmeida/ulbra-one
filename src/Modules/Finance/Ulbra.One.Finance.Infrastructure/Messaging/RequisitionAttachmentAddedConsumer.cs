using MassTransit;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Budgeting.Application.Messaging;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.FiscalDocuments;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Infrastructure.Messaging;

/// <summary>Propagates an attachment added to a requisition onto its Payable, when one exists (the self-service
/// Nota Fiscal loop, AEL-865). The rule is Payable-existence, not status: look up the Payable by
/// <see cref="Payable.OriginRequisitionId"/> — if it exists, carry the file onto it as a regular attachment (no
/// fiscal promotion — the payable is already classified/booked), clearing the "NF pendente" pendency; if it does
/// NOT exist (the requisition never generated one, e.g. still pre-approval), do nothing. The existence check must
/// live here because only Finance can read the Payable. When the payable had a pending "Solicitar NF" request,
/// notifies the Contábil user who asked (<see cref="RequisitionFiscalDocumentAttached"/>). Idempotent: the copier
/// skips files already carried, so at-least-once redelivery is a no-op (and the notify only rides a real add).</summary>
public sealed class RequisitionAttachmentAddedConsumer(
    IUnitOfWork uow, IRequisitionAttachmentCopier attachmentCopier, IPublishEndpoint publish, TimeProvider clock)
    : IConsumer<RequisitionAttachmentAdded>
{
    public Task Consume(ConsumeContext<RequisitionAttachmentAdded> context) =>
        ProcessAsync(context.Message, context.CancellationToken);

    /// <summary>Core logic, extracted for direct testing without a broker.</summary>
    public async Task ProcessAsync(RequisitionAttachmentAdded m, CancellationToken ct)
    {
        // Tracked load with the attachments so the copier's dedup guard sees what is already carried. A recurring
        // requisition maps to N payables (AEL-877); a generic post-approval attachment targets the BASE payable
        // (cycle null) deterministically — for a non-recurring requisition that is simply its single payable.
        var payable = await uow.Set<Payable>()
            .Include(p => p.Attachments)
            .FirstOrDefaultAsync(p => p.OriginRequisitionId == m.RequisitionId && p.OriginCycleCompetency == null, ct);
        // No Payable for this requisition → nothing to propagate onto (the file still lives on the requisition).
        if (payable is null)
            return;

        var a = m.Attachment;
        var added = await attachmentCopier.CopyOntoPayableAsync(payable,
            [new RequisitionAttachmentToCopy(a.FileName, a.ContentType, a.SizeBytes, a.StoragePath, a.UploadedById, RequisitionAttachmentKindParsing.Parse(a.Kind))],
            m.RequisitionId, promoteFiscalXml: false, ct);
        if (added == 0)
            return;

        await uow.SaveChangesAsync(ct);

        // If the Contábil had requested this NF, notify them back that it arrived. Published AFTER SaveChanges
        // (this endpoint has the EF consumer outbox, so it commits atomically) and only when a file was actually
        // carried (added > 0), so a redelivery — which finds the file already present and adds nothing — never
        // re-notifies. PayableId is the notification's idempotency key.
        if (payable.FiscalDocumentRequestedBy is { } requestedBy)
            await publish.Publish(
                new RequisitionFiscalDocumentAttached(
                    payable.CompanyId, requestedBy, payable.OriginRequisitionCode, m.RequisitionId, payable.Id, clock.GetUtcNow()),
                ct);
    }
}
