using MassTransit;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Budgeting.Domain;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Messaging;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Budgeting.Infrastructure.Messaging;

/// <summary>Backs out a launched recurrence cycle when its Finance Payable is cancelled (AEL-877): the invoice
/// will never be paid, so the cycle must stop counting toward the recurrence's settlement and its competency
/// becomes free to relaunch. The cancelled Payable itself tells us which (requisition, competency) it
/// materialized — a loose cross-module read (single physical DB, same pattern as the Finance consumers reading
/// the Requisition) — so the back-out works even if the link callback hasn't stamped the cycle yet. Base/manual
/// payables carry no cycle competency → no-op; a fully-cancelled requisition keeps its rows as history (the
/// cancel consumer already handled it). Idempotent: a redelivery finds no cycle and no-ops.</summary>
public sealed class CyclePayableCancelledConsumer(IUnitOfWork uow, TimeProvider clock)
    : IConsumer<PayableCancelled>
{
    public Task Consume(ConsumeContext<PayableCancelled> context) =>
        ProcessAsync(context.Message, context.CancellationToken);

    /// <summary>Core logic, extracted for direct testing without a broker.</summary>
    public async Task ProcessAsync(PayableCancelled m, CancellationToken ct)
    {
        var origin = await uow.ReadSet<Payable>()
            .Where(p => p.Id == m.PayableId && p.OriginRequisitionId != null && p.OriginCycleCompetency != null)
            .Select(p => new { p.OriginRequisitionId, p.OriginCycleCompetency })
            .FirstOrDefaultAsync(ct);
        if (origin is null) return; // not a cycle payable — nothing to back out

        var requisition = await uow.Set<Requisition>()
            .Include(r => r.Cycles)
            .Include(r => r.Payments) // AllCyclesSettled sums the realizado (may settle a terminated recurrence)
            .Include(r => r.Transitions)
            .FirstOrDefaultAsync(r => r.Id == origin.OriginRequisitionId, ct);
        if (requisition is null) return;
        if (requisition.Status == RequisitionStatus.Cancelled) return; // whole requisition cancelled — keep history

        var transitionsBefore = requisition.Transitions.Count;
        if (!requisition.RemoveCycleForCancelledPayable(origin.OriginCycleCompetency!, m.PayableId, clock.GetUtcNow())) return;

        // The removed cycle is a required child of a tracked parent → EF deletes the orphan row on save. New
        // trail entries are appended explicitly as Added (store-generated-key heuristic, same as the endpoints).
        for (var i = transitionsBefore; i < requisition.Transitions.Count; i++)
            await uow.AddAsync(requisition.Transitions[i], ct);
        await uow.SaveChangesAsync(ct);
    }
}
