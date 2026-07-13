using MassTransit;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Messaging;
using Ulbra.One.Finance.Application.Withholdings;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Infrastructure.Messaging;

/// <summary>Recomputes withholdings after a payable edit. Idempotent by construction: the event
/// carries no data — the recalculation is deterministic on the payable's CURRENT state, and
/// <see cref="IAutoWithholdingService"/> no-ops on Scheduled/Paid and on undetermined type. Safe
/// under at-least-once delivery and out-of-order rapid edits (all deliveries converge to the
/// latest committed state).</summary>
public sealed class PayableUpdatedConsumer(IUnitOfWork uow, IAutoWithholdingService autoWithholding)
    : IConsumer<PayableUpdated>
{
    public Task Consume(ConsumeContext<PayableUpdated> context) =>
        ProcessAsync(context.Message.PayableId, context.CancellationToken);

    /// <summary>Core logic extracted for direct testing without a broker.</summary>
    public async Task ProcessAsync(Guid payableId, CancellationToken ct)
    {
        var payable = await uow.Set<Payable>().FirstOrDefaultAsync(p => p.Id == payableId, ct);
        if (payable is null) return; // deleted between edit and delivery — nothing to do

        await autoWithholding.RecalculateAndApplyAsync(payable, ct);
        // Payable/Installment carry xmin rowversions: a human edit racing this consumer surfaces as
        // DbUpdateConcurrencyException here → message retry re-runs against fresh state (no catch needed).
        await uow.SaveChangesAsync(ct);
    }
}
