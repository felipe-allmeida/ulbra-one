using MassTransit;
using Ulbra.One.Accounting.Application.Posting;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Messaging;

namespace Ulbra.One.Accounting.Infrastructure.Messaging;

public sealed class AccountingReversalConsumer(IPayablePostingService posting, IUnitOfWork uow)
    : IConsumer<PaymentReversed>, IConsumer<PayableCancelled>
{
    public async Task Consume(ConsumeContext<PaymentReversed> ctx)
    {
        var m = ctx.Message;
        await posting.ReverseAsync(
            m.CompanyId,
            $"payment:{m.PaymentId}",
            "Estorno de pagamento",
            ctx.CancellationToken);
        await uow.SaveChangesAsync(ctx.CancellationToken);
    }

    public async Task Consume(ConsumeContext<PayableCancelled> ctx)
    {
        var m = ctx.Message;
        await posting.ReverseAsync(
            m.CompanyId,
            $"payable-incurral:{m.PayableId}",
            "Cancelamento de título",
            ctx.CancellationToken);
        await uow.SaveChangesAsync(ctx.CancellationToken);
    }
}
