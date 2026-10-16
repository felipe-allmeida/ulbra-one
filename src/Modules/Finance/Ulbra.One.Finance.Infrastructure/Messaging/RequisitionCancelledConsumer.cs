using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ulbra.One.Budgeting.Application.Messaging;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Messaging;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Infrastructure.Messaging;

/// <summary>Cancels the Payable generated from a requisition when that requisition is cancelled (AEL-855). Listens
/// on the generic <see cref="RequisitionTransitioned"/> and acts only on the Cancelled transition. Idempotent
/// (a payable already Cancelled → no-op). A Paid or partially-paid payable can't be cancelled here — money has
/// moved and a human must reverse it — so it's logged, not thrown (a retry can't change that state). Publishes
/// <see cref="PayableCancelled"/> so Accounting reverses the incurral, matching the manual CancelPayable path.</summary>
public sealed class RequisitionCancelledConsumer(
    IUnitOfWork uow, IPublishEndpoint publish, ILogger<RequisitionCancelledConsumer> logger)
    : IConsumer<RequisitionTransitioned>
{
    public Task Consume(ConsumeContext<RequisitionTransitioned> context) =>
        ProcessAsync(context.Message, context.CancellationToken);

    /// <summary>Core logic, extracted for direct testing without a broker.</summary>
    public async Task ProcessAsync(RequisitionTransitioned m, CancellationToken ct)
    {
        if (m.ToStatus != nameof(Budgeting.Domain.RequisitionStatus.Cancelled)) return;

        // A recurring requisition maps to N payables (base + monthly cycles, AEL-877) — cancel each cancellable
        // one; a Paid/partially-paid title is logged and skipped (money moved, a human must reverse it).
        var payables = await uow.Set<Payable>()
            .Include(p => p.Installments).ThenInclude(i => i.Payments) // Cancel() inspects registered payments
            .Where(p => p.OriginRequisitionId == m.RequisitionId).ToListAsync(ct);

        foreach (var payable in payables)
        {
            if (payable.Status == PayableStatus.Cancelled) continue; // idempotent

            if (payable.Status == PayableStatus.Paid
                || payable.Installments.Any(i => i.Payments.Any(p => !p.IsReversed)))
            {
                logger.LogWarning(
                    "Requisição {Code} cancelada, mas o título {PayableId} tem pagamentos (status {Status}); estorno manual necessário antes do cancelamento do título.",
                    m.Code, payable.Id, payable.Status);
                continue; // never throw: retry cannot change this state
            }

            payable.Cancel(string.IsNullOrWhiteSpace(m.Reason)
                ? $"Requisição {m.Code} cancelada"
                : $"Requisição {m.Code} cancelada: {m.Reason}");
            // Publish before SaveChanges — the consumer EF outbox commits the events with the cancelled rows.
            await publish.Publish(new PayableCancelled(payable.Id, payable.CompanyId), ct);
        }

        await uow.SaveChangesAsync(ct);
    }
}
