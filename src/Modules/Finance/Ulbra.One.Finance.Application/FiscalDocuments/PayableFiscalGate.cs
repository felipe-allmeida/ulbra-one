using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.FiscalDocuments;

/// <summary>Single source of truth for the "documento fiscal presente" gate (AEL-959). A payable qualifies when it
/// has a parsed <see cref="FiscalDocument"/> (XML) or a <see cref="PayableAttachment"/> typed as a Nota Fiscal;
/// for Conta Fixa (<see cref="Payable.RequiresFiscalDocument"/>) a <see cref="PayableAttachmentKind.Invoice"/>
/// (fatura) also qualifies (Fatura OU NF). Advance payables are exempt and handled by the caller. The read models
/// (StageItems/StageSummary/AccountingView/ListPayables) mirror this exact rule inline in SQL.</summary>
public static class PayableFiscalGate
{
    public static async Task<bool> HasQualifyingDocumentAsync(IUnitOfWork uow, Payable payable, CancellationToken ct)
    {
        if (await uow.ReadSet<FiscalDocument>().AnyAsync(d => d.PayableId == payable.Id, ct))
            return true;
        if (await uow.ReadSet<PayableAttachment>().AnyAsync(a => a.PayableId == payable.Id && a.Kind == PayableAttachmentKind.FiscalDocument, ct))
            return true;
        return payable.RequiresFiscalDocument
            && await uow.ReadSet<PayableAttachment>().AnyAsync(a => a.PayableId == payable.Id && a.Kind == PayableAttachmentKind.Invoice, ct);
    }
}
