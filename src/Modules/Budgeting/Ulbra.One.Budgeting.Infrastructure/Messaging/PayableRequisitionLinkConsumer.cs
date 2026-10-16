using MassTransit;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Messaging;

namespace Ulbra.One.Budgeting.Infrastructure.Messaging;

/// <summary>Records, on the requisition, the Payable that Finance created for it (AEL-877) — so the requisition
/// screen can list the Payables it generated. Matches on the cycle competency: <c>null</c> → the base Payable
/// (<see cref="Requisition.BasePayableId"/>); a "YYYY-MM" → the matching <see cref="RequisitionCycle.PayableId"/>.
/// Idempotent (Link* is first-wins), so at-least-once redelivery is safe. No-op if the requisition/cycle is
/// missing.</summary>
public sealed class PayableRequisitionLinkConsumer(IUnitOfWork uow)
    : IConsumer<PayableCreatedForRequisition>
{
    public Task Consume(ConsumeContext<PayableCreatedForRequisition> context) =>
        ProcessAsync(context.Message, context.CancellationToken);

    /// <summary>Core logic, extracted for direct testing without a broker.</summary>
    public async Task ProcessAsync(PayableCreatedForRequisition m, CancellationToken ct)
    {
        var requisition = await uow.Set<Requisition>()
            .Include(r => r.Cycles)
            .FirstOrDefaultAsync(r => r.Id == m.RequisitionId, ct);
        if (requisition is null) return;

        if (m.CycleCompetency is null)
        {
            requisition.LinkBasePayable(m.PayableId);
        }
        else
        {
            var cycle = requisition.Cycles.FirstOrDefault(c => c.Competency == m.CycleCompetency);
            if (cycle is null) return; // cycle not found (shouldn't happen) — nothing to link
            cycle.LinkPayable(m.PayableId);
        }

        await uow.SaveChangesAsync(ct);
    }
}
