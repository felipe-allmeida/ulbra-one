using MassTransit;
using Ulbra.One.Accounting.Application.Posting;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Messaging;

namespace Ulbra.One.Accounting.Infrastructure.Messaging;

/// <summary>Rebooks the payable incurral when Contábil reclassifies its expense account (post-fato): reverse the
/// current posting + repost under a versioned sourceKey (posted case), or drive the parked pending (parked case).
/// Idempotent (see <see cref="IPayablePostingService.ReclassifyIncurralAsync"/>).</summary>
public sealed class PayableReclassifiedConsumer(IPayablePostingService posting, IUnitOfWork uow)
    : IConsumer<PayableReclassified>
{
    public async Task Consume(ConsumeContext<PayableReclassified> ctx)
    {
        var m = ctx.Message;
        var withholdings = m.Withholdings
            .Select(w => (w.TaxType, w.Amount))
            .ToList();
        await posting.ReclassifyIncurralAsync(
            m.CompanyId,
            m.PayableId,
            m.AccountingAccountId,
            m.Amount,
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
