using MassTransit;
using Ulbra.One.Accounting.Application.Posting;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Messaging;

namespace Ulbra.One.Accounting.Infrastructure.Messaging;

public sealed class PaymentRegisteredConsumer(IPayablePostingService posting, IUnitOfWork uow)
    : IConsumer<PaymentRegistered>
{
    public async Task Consume(ConsumeContext<PaymentRegistered> ctx)
    {
        var m = ctx.Message;
        await posting.PostSettlementAsync(
            m.CompanyId,
            $"payment:{m.PaymentId}",
            m.Amount,
            m.PaymentDate,
            m.CashAccountId,
            ctx.CancellationToken);
        await uow.SaveChangesAsync(ctx.CancellationToken);
    }
}
