using MassTransit;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Budgeting.Application.Messaging;
using Ulbra.One.Budgeting.Domain;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Messaging;

namespace Ulbra.One.Budgeting.Infrastructure.Messaging;

/// <summary>Returns a requisition to the requester (Complement) or controllership (Triage) when Finance rejects
/// its generated payable (AEL-935). Idempotent: only acts while the requisition is still in a payment stage, so a
/// redelivery — or a title rejected after it already moved on — no-ops. Publishes the transition so the requester
/// is notified with the reason.</summary>
public sealed class PayableRejectedConsumer(IUnitOfWork uow, IPublishEndpoint publish, TimeProvider clock)
    : IConsumer<PayableRejected>
{
    public Task Consume(ConsumeContext<PayableRejected> context) =>
        ProcessAsync(context.Message, context.CancellationToken);

    /// <summary>Core logic, extracted for direct testing without a broker.</summary>
    public async Task ProcessAsync(PayableRejected m, CancellationToken ct)
    {
        var requisition = await uow.Set<Requisition>()
            .Include(r => r.Transitions)
            .FirstOrDefaultAsync(r => r.Id == m.RequisitionId, ct);
        if (requisition is null) return;
        if (requisition.Status is not (RequisitionStatus.InPayment or RequisitionStatus.AwaitingFiscalDocument)) return;

        var target = Enum.TryParse<RequisitionReturnTarget>(m.ReturnTarget, out var t) ? t : RequisitionReturnTarget.Requester;
        requisition.ReturnFromPayment(target, Guid.Parse(m.RejectedBy), clock.GetUtcNow(), m.Reason);

        await uow.AddAsync(requisition.Transitions[^1], ct); // explicit Added (store-generated-key heuristic)
        await requisition.PublishLastTransition(publish, ct); // notify the requester (Notifications module)
        await uow.SaveChangesAsync(ct);
    }
}
