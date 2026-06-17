using MassTransit;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Messaging.Contracts;
using Ulbra.One.Notifications.Domain.Entities;

namespace Ulbra.One.Notifications.Application.Consumers;

public sealed class PayablePendingApprovalConsumer(IUnitOfWork uow, TimeProvider clock) : IConsumer<PayablePendingApproval>
{
    public async Task Consume(ConsumeContext<PayablePendingApproval> ctx)
    {
        var m = ctx.Message;
        var ct = ctx.CancellationToken;

        // Idempotency guard: skip if a notification for this payable already exists.
        var exists = await uow.ReadSet<Notification>()
            .AnyAsync(n => n.ReferenceId == m.PayableId && n.Type == "PayablePendingApproval", ct);
        if (exists) return;

        var notification = new Notification(
            m.CompanyId,
            "Admin",
            "PayablePendingApproval",
            "Conta pendente de aprovação",
            $"{m.VendorName} — R$ {m.TotalAmount:N2}",
            m.PayableId,
            clock.GetUtcNow());

        await uow.AddAsync(notification, ct);
        await uow.SaveChangesAsync(ct);
    }
}
