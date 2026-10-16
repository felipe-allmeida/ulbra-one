using MassTransit;
using Ulbra.One.Accounting.Application.Posting;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Messaging;

namespace Ulbra.One.Accounting.Infrastructure.Messaging;

/// <summary>Refreshes the número da NF (SourceReference) shown in the Livro Razão on the payable's already-posted
/// incurral + settlement entries when Contábil edits the document number post-schedule/post-payment. Metadata-only
/// amend (no rebook/reversal); idempotent (see <see cref="IPayablePostingService.AmendDocumentReferenceAsync"/>).</summary>
public sealed class PayableDocumentNumberChangedConsumer(IPayablePostingService posting, IUnitOfWork uow)
    : IConsumer<PayableDocumentNumberChanged>
{
    public async Task Consume(ConsumeContext<PayableDocumentNumberChanged> ctx)
    {
        var m = ctx.Message;
        await posting.AmendDocumentReferenceAsync(
            m.CompanyId, m.PayableId, m.PaymentIds ?? [], m.DocumentNumber, ctx.CancellationToken);
        await uow.SaveChangesAsync(ctx.CancellationToken);
    }
}
