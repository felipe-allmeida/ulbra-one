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

/// <summary>Creates one additional Payable for a monthly cycle of a recurring requisition (AEL-877, the "fatura
/// do mês"). Idempotent: dedups on the (<see cref="Payable.OriginRequisitionId"/>, <see cref="Payable.OriginCycleCompetency"/>)
/// pair (backed by a partial unique index), so at-least-once redelivery never generates a second Payable for the
/// same competency. The cycle Payable rides the normal Finance pipeline via <see cref="PayableReceived"/> — same
/// classification/withholdings/agendamento as any other. No re-approval on the requisition side.</summary>
public sealed class RequisitionCycleAddedForPaymentConsumer(
    IUnitOfWork uow, IPublishEndpoint publish, IRequisitionAttachmentCopier attachmentCopier,
    ILogger<RequisitionCycleAddedForPaymentConsumer> logger)
    : IConsumer<RequisitionCycleAddedForPayment>
{
    public Task Consume(ConsumeContext<RequisitionCycleAddedForPayment> context) =>
        ProcessAsync(context.Message, context.CancellationToken);

    /// <summary>Core logic, extracted for direct testing without a broker.</summary>
    public async Task ProcessAsync(RequisitionCycleAddedForPayment m, CancellationToken ct)
    {
        // Idempotency on (requisition, cycle competency): a redelivery finds the existing cycle Payable and is a
        // no-op, self-healing it if it got stuck in Received (lost original PayableReceived). Cancelled payables
        // don't count — a relaunch after a cancellation creates a fresh Payable (the partial unique index also
        // ignores them).
        var existing = await uow.ReadSet<Payable>()
            .FirstOrDefaultAsync(p => p.OriginRequisitionId == m.RequisitionId && p.OriginCycleCompetency == m.Competency
                && p.Status != PayableStatus.Cancelled, ct);
        if (existing is not null)
        {
            if (existing.Status == PayableStatus.Received)
                await publish.Publish(new PayableReceived(existing.Id, m.CompanyId), ct);
            return;
        }

        // Creation-race guard: don't materialize a cycle Payable for an already-cancelled requisition.
        if (await IsRequisitionCancelledAsync(m.RequisitionId, ct))
        {
            logger.LogInformation("Requisition {RequisitionId} was cancelled before its cycle {Competency} Payable was created — skipping.", m.RequisitionId, m.Competency);
            return;
        }

        // The cycle row may have been backed out meanwhile (its earlier Payable was cancelled and the return path
        // removed it) — a redelivered message must not resurrect a cycle the requisition no longer has. Loose
        // cross-module read by id, same single-DB pattern as IsRequisitionCancelledAsync above.
        if (!await uow.ReadSet<RequisitionCycle>()
                .AnyAsync(c => c.RequisitionId == m.RequisitionId && c.Competency == m.Competency, ct))
        {
            logger.LogInformation("Requisition {RequisitionId} no longer has a cycle for {Competency} — skipping its Payable.", m.RequisitionId, m.Competency);
            return;
        }

        // Vendors are global to the organization — resolve by id only.
        var vendor = await uow.ReadSet<VendorEntity>()
            .FirstOrDefaultAsync(v => v.Id == m.VendorId, ct);
        if (vendor is null)
            throw new InvalidOperationException(
                $"Vendor {m.VendorId} not found (requisition {m.RequisitionId}, cycle {m.Competency}).");

        var i = m.Installment;
        var installments = new List<Installment>
        {
            new(i.Sequence, i.DueDate, i.Amount, boletoDigitableLine: i.BoletoDigitableLine, paymentMethod: ParsePaymentMethod(i.PaymentMethod), pixKeyOverride: i.PixKeyOverride, pixPayloadEmv: i.PixPayloadEmv),
        };

        var payable = new Payable(m.CompanyId, PayableOrigin.Purchase, vendor.Id, vendor.Name,
            documentNumber: null, IssueDateFrom(m.Competency), m.Description, m.CostCenterId,
            categoryId: null, installments);
        payable.SetOrigin(m.RequisitionId, m.UnitId, m.Code);
        payable.SetCycle(m.Competency);
        // Conta Fixa (AEL-881): the cycle invoice requires its Nota Fiscal before scheduling.
        if (m.RequiresFiscalDocument) payable.RequireFiscalDocument();
        if (m.RequiresTaxAnalysis) payable.RequireTaxAnalysis();

        if (Enum.TryParse<PayableType>(m.PayableType, ignoreCase: true, out var payableType))
            payable.SetType(payableType);
        else
            logger.LogWarning("Requisition {RequisitionId} cycle {Competency}: unrecognized PayableType '{PayableType}'.",
                m.RequisitionId, m.Competency, m.PayableType);

        var schedule = vendor.PaymentSchedule ?? PaymentSchedule.Default;
        foreach (var inst in payable.Installments)
            inst.SetScheduledPaymentDate(schedule.Resolve(inst.DueDate));

        // NF anexada no próprio lançamento da fatura (AEL-877): copia o arquivo para ESTE Payable do ciclo (blob
        // próprio do Finance; XML promovido a documento fiscal quando aplicável), assim ele já nasce com a NF e sai
        // da pendência. Mesmo caminho do Payable-base na aprovação. Ausente → o título fica em "NF pendente" (R3).
        if (m.Attachment is { } att)
            await attachmentCopier.CopyOntoPayableAsync(payable,
                [new RequisitionAttachmentToCopy(att.FileName, att.ContentType, att.SizeBytes, att.StoragePath, att.UploadedById, RequisitionAttachmentKindParsing.Parse(att.Kind))],
                m.RequisitionId, promoteFiscalXml: true, ct);

        await uow.AddAsync(payable, ct);
        await uow.SaveChangesAsync(ct);

        // Close the residual window (requisition cancelled during creation) — same self-cancel as the base consumer.
        if (await IsRequisitionCancelledAsync(m.RequisitionId, ct))
        {
            payable.Cancel($"Solicitação {m.RequisitionId} cancelada durante a criação do título do ciclo {m.Competency}.");
            await uow.SaveChangesAsync(ct);
            return;
        }

        // Publish AFTER SaveChanges (EF consumer outbox commits it atomically; direct-drive tests observe the row).
        await publish.Publish(new PayableReceived(payable.Id, m.CompanyId), ct);
        // Return link so Budgeting can attach this cycle's Payable to the RequisitionCycle (AEL-877).
        await publish.Publish(new PayableCreatedForRequisition(m.RequisitionId, payable.Id, m.Competency, m.CompanyId), ct);
    }

    private async Task<bool> IsRequisitionCancelledAsync(Guid requisitionId, CancellationToken ct)
    {
        var status = await uow.ReadSet<Requisition>()
            .Where(r => r.Id == requisitionId)
            .Select(r => (RequisitionStatus?)r.Status)
            .FirstOrDefaultAsync(ct);
        return status == RequisitionStatus.Cancelled;
    }

    private static PaymentMethod? ParsePaymentMethod(string? value) =>
        Enum.TryParse<PaymentMethod>(value, ignoreCase: true, out var method) ? method : null;

    private static DateOnly IssueDateFrom(string competency)
    {
        var parts = competency.Split('-');
        return parts.Length == 2 && int.TryParse(parts[0], out var year) && int.TryParse(parts[1], out var month)
            ? new DateOnly(year, month, 1)
            : DateOnly.FromDateTime(DateTime.UnixEpoch); // unreachable: competency is validated in the domain
    }
}
