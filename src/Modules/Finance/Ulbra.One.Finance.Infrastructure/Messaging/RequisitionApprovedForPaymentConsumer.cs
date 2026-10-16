using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ulbra.One.Budgeting.Application.Messaging;
using Ulbra.One.Budgeting.Domain;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain;
using Ulbra.One.Finance.Application.FiscalDocuments;
using Ulbra.One.Finance.Application.Messaging;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Registry.Domain.Entities;
using Ulbra.One.Registry.Domain.ValueObjects;

namespace Ulbra.One.Finance.Infrastructure.Messaging;

/// <summary>Creates exactly one Payable from an approved requisition (AEL-793, the "ida" of the integration).
/// Idempotent: dedups on the stamped <see cref="Payable.OriginRequisitionId"/> (backed by a unique index), so
/// at-least-once redelivery never generates a second Payable. When a requisition attachment is a Nota Fiscal XML
/// it's promoted to the Payable's fiscal document so the classification/withholdings pipeline sees CFOP/NCM/LC116
/// (PDF/image attachments are never auto-read). The Payable then rides the normal Finance pipeline via
/// <see cref="PayableReceived"/>. If the existing title was rejected (returned to Budgeting, AEL-935), a re-approval
/// refreshes it in place instead of creating a second one.</summary>
public sealed class RequisitionApprovedForPaymentConsumer(
    IUnitOfWork uow, IPublishEndpoint publish, IRequisitionAttachmentCopier attachmentCopier,
    ILogger<RequisitionApprovedForPaymentConsumer> logger)
    : IConsumer<RequisitionApprovedForPayment>
{
    public Task Consume(ConsumeContext<RequisitionApprovedForPayment> context) =>
        ProcessAsync(context.Message, context.CancellationToken);

    /// <summary>Core logic, extracted for direct testing without a broker (same shape as
    /// <c>PayableReceivedConsumer.ProcessAsync</c>).</summary>
    public async Task ProcessAsync(RequisitionApprovedForPayment m, CancellationToken ct)
    {
        // Idempotency: a Payable already exists for this requisition. Normally a no-op (redelivery / duplicate).
        // Received → re-publish PayableReceived (self-heal for a lost create-time publish; the consumer is idempotent).
        // Rejected → the requisition was returned and corrected, so refresh the same title in place (AEL-935).
        var existing = await uow.ReadSet<Payable>()
            .FirstOrDefaultAsync(p => p.OriginRequisitionId == m.RequisitionId && p.OriginCycleCompetency == null, ct);
        if (existing is not null)
        {
            if (existing.Status == PayableStatus.Received)
                await publish.Publish(new PayableReceived(existing.Id, m.CompanyId), ct);
            else if (existing.Status == PayableStatus.Rejected)
                await RefreshRejectedAsync(existing.Id, m, ct);
            return;
        }

        // Creation-race guard: a cancel published after this event would leave an orphan Payable. Skip creation if
        // the requisition is already cancelled. Loose cross-module read by id (single-DB pattern, as VendorEntity below).
        if (await IsRequisitionCancelledAsync(m.RequisitionId, ct))
        {
            logger.LogInformation("Requisition {RequisitionId} was cancelled before its Payable was created — skipping creation.", m.RequisitionId);
            return;
        }

        var vendor = await ResolveVendorAsync(m, ct);

        var payable = new Payable(m.CompanyId, PayableOrigin.Purchase, vendor.Id, vendor.Name,
            documentNumber: null, m.IssueDate ?? IssueDateFrom(m.Competency), m.Description, m.CostCenterId,
            categoryId: null, BuildInstallments(m));
        payable.SetOrigin(m.RequisitionId, m.UnitId, m.Code);

        await HydratePayableAsync(payable, m, vendor, ct);

        await uow.AddAsync(payable, ct);
        await uow.SaveChangesAsync(ct);

        // Close the residual window: the requisition may have been cancelled while this Payable was being created.
        // If so, cancel the freshly-created Payable and don't publish PayableReceived (nothing was scheduled).
        if (await IsRequisitionCancelledAsync(m.RequisitionId, ct))
        {
            payable.Cancel($"Requisição {m.RequisitionId} cancelada durante a criação do título.");
            await uow.SaveChangesAsync(ct);
            return;
        }

        // Publish AFTER SaveChanges: this endpoint has the EF consumer outbox (AEL-845), so delivery is atomic.
        await publish.Publish(new PayableReceived(payable.Id, m.CompanyId), ct);
        // Return link so Budgeting can show the Payable this requisition generated (AEL-877). Null cycle = base.
        await publish.Publish(new PayableCreatedForRequisition(m.RequisitionId, payable.Id, null, m.CompanyId), ct);
    }

    /// <summary>Refreshes the rejected base Payable in place from a re-approval: reopens it, re-hydrates the
    /// corrected requisition data, re-syncs the NF/attachments, and re-publishes PayableReceived so it re-enters
    /// the pipeline. Keeps the same PayableId (the vínculo with the requisition is preserved) — AEL-935.</summary>
    private async Task RefreshRejectedAsync(Guid payableId, RequisitionApprovedForPayment m, CancellationToken ct)
    {
        var payable = await uow.Set<Payable>()
            .Include(p => p.Attachments) // the copier dedups against files already on the payable
            .FirstOrDefaultAsync(p => p.Id == payableId, ct);
        if (payable is null) return;

        var vendor = await ResolveVendorAsync(m, ct);
        payable.Reopen(); // Rejected → Received so the mutations below pass EnsureMutable

        // Full-replace installments + clear stale retentions; reconcile EF explicitly (private lists aren't tracked).
        var oldInstallments = payable.Installments.ToList();
        var oldWithholdings = payable.Withholdings.ToList();
        payable.Update(vendor.Id, vendor.Name, documentNumber: null, m.IssueDate ?? IssueDateFrom(m.Competency),
            m.Description, m.CostCenterId, categoryId: null, BuildInstallments(m));
        payable.SetWithholdings([]); // the pipeline recomputes them on PayableReceived
        uow.RemoveRange(oldInstallments);
        uow.RemoveRange(oldWithholdings);
        foreach (var inst in payable.Installments) await uow.AddAsync(inst, ct);

        // Re-sync the NF: drop a stale fiscal doc first (unique per payable) so the copier can re-promote.
        var docs = await uow.Set<FiscalDocument>().Where(d => d.PayableId == payable.Id).ToListAsync(ct);
        if (docs.Count > 0) { uow.RemoveRange(docs); await uow.SaveChangesAsync(ct); }

        await HydratePayableAsync(payable, m, vendor, ct);
        await uow.SaveChangesAsync(ct);
        await publish.Publish(new PayableReceived(payable.Id, m.CompanyId), ct);
    }

    /// <summary>Applies the requisition data shared by the create and refresh paths: Service/Product type (drives
    /// withholdings; an NF ingested below may override it), the Conta Fixa NF requirement (AEL-881), the vendor's
    /// payment schedule per installment, and the requisition attachments (promoting a Nota Fiscal XML).</summary>
    private async Task HydratePayableAsync(Payable payable, RequisitionApprovedForPayment m, VendorEntity vendor, CancellationToken ct)
    {
        if (Enum.TryParse<PayableType>(m.PayableType, ignoreCase: true, out var payableType))
            payable.SetType(payableType);
        else
            logger.LogWarning("Requisition {RequisitionId}: unrecognized PayableType '{PayableType}' — payable left untyped (manual esteira / NF ingestion will set it).",
                m.RequisitionId, m.PayableType);
        if (m.RequiresFiscalDocument) payable.RequireFiscalDocument();
        if (m.RequiresTaxAnalysis) payable.RequireTaxAnalysis();

        var schedule = vendor.PaymentSchedule ?? PaymentSchedule.Default;
        foreach (var inst in payable.Installments)
            inst.SetScheduledPaymentDate(schedule.Resolve(inst.DueDate));

        // Promotion happens BEFORE PayableReceived so the classification/withholdings pipeline sees CFOP/NCM/LC116.
        await attachmentCopier.CopyOntoPayableAsync(payable,
            [.. m.Attachments.Select(a => new RequisitionAttachmentToCopy(a.FileName, a.ContentType, a.SizeBytes, a.StoragePath, a.UploadedById, RequisitionAttachmentKindParsing.Parse(a.Kind)))],
            m.RequisitionId, promoteFiscalXml: true, ct);
    }

    private static List<Installment> BuildInstallments(RequisitionApprovedForPayment m) =>
        m.Installments
            .OrderBy(i => i.Sequence)
            .Select(i => new Installment(i.Sequence, i.DueDate, i.Amount, boletoDigitableLine: i.BoletoDigitableLine, paymentMethod: ParsePaymentMethod(i.PaymentMethod), pixKeyOverride: i.PixKeyOverride))
            .ToList();

    /// <summary>Resolves the message's vendor (vendors are global to the organization); throws (→ redeliver) if it
    /// vanished after approval so the Payable isn't silently dropped.</summary>
    private async Task<VendorEntity> ResolveVendorAsync(RequisitionApprovedForPayment m, CancellationToken ct) =>
        await uow.ReadSet<VendorEntity>().FirstOrDefaultAsync(v => v.Id == m.VendorId, ct)
        ?? throw new InvalidOperationException($"Vendor {m.VendorId} not found (requisition {m.RequisitionId}).");

    /// <summary>Loose cross-module read (single physical DB) of the source requisition's status — used by the
    /// creation-race guards to avoid materializing an orphan Payable for an already-cancelled requisition.</summary>
    private async Task<bool> IsRequisitionCancelledAsync(Guid requisitionId, CancellationToken ct)
    {
        var status = await uow.ReadSet<Requisition>()
            .Where(r => r.Id == requisitionId)
            .Select(r => (RequisitionStatus?)r.Status)
            .FirstOrDefaultAsync(ct);
        return status == RequisitionStatus.Cancelled;
    }

    /// <summary>Parses the per-installment payment method carried as a neutral string (AEL-844); null when
    /// absent or unrecognized (legacy messages / non-authored schedules).</summary>
    private static PaymentMethod? ParsePaymentMethod(string? value) =>
        Enum.TryParse<PaymentMethod>(value, ignoreCase: true, out var method) ? method : null;

    /// <summary>Default IssueDate for a requisition-sourced payable: the first day of the requisition's start
    /// competency ("YYYY-MM"). Overridden by the authored emission date (AEL-844) or the Nota Fiscal's own.</summary>
    private static DateOnly? IssueDateFrom(string competency)
    {
        var parts = competency.Split('-');
        return parts.Length == 2 && int.TryParse(parts[0], out var year) && int.TryParse(parts[1], out var month)
            ? new DateOnly(year, month, 1)
            : null;
    }
}
