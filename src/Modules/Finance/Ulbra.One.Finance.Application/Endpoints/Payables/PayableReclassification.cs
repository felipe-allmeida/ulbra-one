using MassTransit;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Messaging;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables;

/// <summary>Shared reclassify-and-publish for a scheduled/paid payable, so the classify and reclassify endpoints
/// can't drift. Always publishes <see cref="PayableReclassified"/> (before SaveChanges, via the outbox), even when
/// the account is unchanged: <c>ReclassifyIncurralAsync</c> is idempotent (posted-same-account → no-op; parked →
/// post + resolve), so republishing lets a parked/poisoned incurral self-heal.</summary>
internal static class PayableReclassification
{
    public static async Task ReclassifyAndPublishAsync(
        Payable payable, Guid accountingAccountId, IUnitOfWork uow, IPublishEndpoint publish, CancellationToken ct)
    {
        payable.Reclassify(accountingAccountId); // return value deliberately ignored — see summary

        // Supplier identification for the Livro Razão history (same pattern as ReclassifyPayable/CompleteAccounting).
        var vendor = await uow.ReadSet<VendorEntity>()
            .Where(v => v.Id == payable.VendorId)
            .Select(v => new { v.Name, v.Document })
            .FirstOrDefaultAsync(ct);
        await publish.Publish(PayableScheduledFactory.BuildReclassified(payable, vendor?.Name, vendor?.Document), ct);
    }
}
