using MassTransit;
using Ulbra.One.Accounting.Application.Posting;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Messaging;

namespace Ulbra.One.Accounting.Infrastructure.Messaging;

public sealed class AccountingReversalConsumer(IPayablePostingService posting, IUnitOfWork uow)
    : IConsumer<PaymentReversed>, IConsumer<PayableCancelled>, IConsumer<PayableRejected>
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
        // Reverse the LIVE incurral (base or :r{n} after a reclassify) — not the hardcoded base key.
        await posting.ReverseIncurralAsync(m.CompanyId, m.PayableId, "Cancelamento de título", ctx.CancellationToken);
        await uow.SaveChangesAsync(ctx.CancellationToken);
    }

    // AEL-935: returning a requisition title reverses its incurral (no-op when nothing was posted — e.g. an
    // unclassified title whose incurral is still parked). Re-booked under a versioned key on re-approval.
    public async Task Consume(ConsumeContext<PayableRejected> ctx)
    {
        var m = ctx.Message;
        await posting.ReverseIncurralAsync(m.CompanyId, m.PayableId, "Devolução de título", ctx.CancellationToken);
        await uow.SaveChangesAsync(ctx.CancellationToken);
    }
}
