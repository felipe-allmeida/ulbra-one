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
        var withholdings = m.Withholdings
            .Select(w => (w.TaxType, w.Amount))
            .ToList();
        // PostOrRebook (not PostIncurral): a title returned and re-approved re-books under a versioned key, since
        // the reversed original still occupies the base key (AEL-935). Idempotent for the normal/redelivery path.
        await posting.PostOrRebookIncurralAsync(
            m.CompanyId,
            m.PayableId,
            m.Amount,
            m.AccountingAccountId,
            withholdings,
            m.CompetenceDate,
            m.CostCenterId,
            m.UnitId,
            m.DocumentNumber,
            m.CounterpartyName,
            m.CounterpartyDocument,
            ctx.CancellationToken);
        await uow.SaveChangesAsync(ctx.CancellationToken);
    }
}
