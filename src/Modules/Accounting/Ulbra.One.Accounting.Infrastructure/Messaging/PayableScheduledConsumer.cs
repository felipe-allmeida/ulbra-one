using MassTransit;
using Ulbra.One.Accounting.Application.Posting;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Messaging;

namespace Ulbra.One.Accounting.Infrastructure.Messaging;

public sealed class PayableScheduledConsumer(IPayablePostingService posting, IUnitOfWork uow)
    : IConsumer<PayableScheduled>
{
    public async Task Consume(ConsumeContext<PayableScheduled> ctx)
    {
        var m = ctx.Message;
        await posting.PostIncurralAsync(
            m.CompanyId,
            $"payable-incurral:{m.PayableId}",
            m.Amount,
            m.AccountingAccountCode,
            ctx.CancellationToken);
        await uow.SaveChangesAsync(ctx.CancellationToken);
    }
}
