using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Audit.Domain;
using Ulbra.One.Audit.Domain.Entities;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.AuditTrail;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.AuditTrail;

public sealed class GetPayableAuditTrail : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("{id}/audit-trail", Handle).WithName("GetPayableAuditTrail").WithTags("Finance")
            .RequireAuthorization().Produces<IReadOnlyList<AuditTrailEventResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(
        PayableId id,
        IUnitOfWork uow,
        [FromKeyedServices("audit")] IUnitOfWork audit,
        IUserDirectory users,
        CancellationToken ct)
    {
        var payable = await uow.ReadSet<Payable>()
            .Include(x => x.Attachments)
            .FirstOrDefaultAsync(x => x.Id == id.Value, ct);
        if (payable is null)
            return TypedResults.Problem(detail: "Payable not found.", statusCode: 404, title: "Payable.NotFound");

        // --- AuditLog-sourced timestamps (hybrid "C"): actions lacking a dedicated entity timestamp. ---
        DateTimeOffset? cancelledAt = null;
        if (payable.Status == PayableStatus.Cancelled)
        {
            var pk = payable.Id.ToString();
            cancelledAt = await audit.ReadSet<AuditLog>()
                .Where(a => a.EntityType == "Payable" && a.PrimaryKey == pk
                            && a.Action == AuditAction.Update && a.AffectedColumns != null
                            && a.AffectedColumns.Contains("CancelReason"))
                .OrderByDescending(a => a.OccurredAt)
                .Select(a => (DateTimeOffset?)a.OccurredAt)
                .FirstOrDefaultAsync(ct);
        }

        var reversedPaymentIds = payable.Installments
            .SelectMany(i => i.Payments).Where(p => p.IsReversed).Select(p => p.Id.ToString()).ToList();
        var paymentReversalTimes = new Dictionary<Guid, DateTimeOffset>();
        if (reversedPaymentIds.Count > 0)
        {
            var rows = await audit.ReadSet<AuditLog>()
                .Where(a => a.EntityType == "Payment" && reversedPaymentIds.Contains(a.PrimaryKey)
                            && a.Action == AuditAction.Update && a.AffectedColumns != null
                            && a.AffectedColumns.Contains("IsReversed"))
                .Select(a => new { a.PrimaryKey, a.OccurredAt })
                .ToListAsync(ct);
            foreach (var g in rows.GroupBy(r => r.PrimaryKey))
                if (Guid.TryParse(g.Key, out var pid))
                    paymentReversalTimes[pid] = g.Max(x => x.OccurredAt);
        }

        // --- Resolve actor names for every Guid actor referenced. ---
        var guidActors = new List<Guid>();
        foreach (var i in payable.Installments)
            foreach (var p in i.Payments)
                guidActors.Add(p.CreatedById);
        foreach (var a in payable.Attachments)
            guidActors.Add(a.UploadedById);
        if (Guid.TryParse(payable.CreatedBy, out var cg)) guidActors.Add(cg);
        if (Guid.TryParse(payable.ApprovedBy, out var ag)) guidActors.Add(ag);
        if (Guid.TryParse(payable.RejectedBy, out var rg)) guidActors.Add(rg);
        if (Guid.TryParse(payable.UpdatedBy, out var ug)) guidActors.Add(ug);

        var names = await users.GetNamesAsync(guidActors, ct);

        var events = PayableAuditTrailBuilder.Build(payable, names, cancelledAt, paymentReversalTimes);
        return TypedResults.Ok(events);
    }
}
