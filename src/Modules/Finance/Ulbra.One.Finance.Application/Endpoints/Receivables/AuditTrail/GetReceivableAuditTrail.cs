using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.AuditTrail;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Receivables.AuditTrail;

public sealed class GetReceivableAuditTrail : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("{id}/audit-trail", Handle).WithName("GetReceivableAuditTrail").WithTags("Finance")
            .RequireAuthorization().Produces<IReadOnlyList<AuditTrailEventResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(
        ReceivableId id, IUnitOfWork uow, IUserDirectory users, CancellationToken ct)
    {
        var receivable = await uow.ReadSet<Receivable>()
            .Include(x => x.Installments)
            .FirstOrDefaultAsync(x => x.Id == id.Value, ct);
        if (receivable is null)
            return TypedResults.Problem(detail: "Receivable not found.", statusCode: 404, title: "Receivable.NotFound");

        var guidActors = new List<Guid>();
        if (receivable.CancelledById is { } cb) guidActors.Add(cb);
        foreach (var i in receivable.Installments)
            foreach (var r in i.Receipts)
            {
                guidActors.Add(r.CreatedById);
                if (r.ReversedById is { } rb) guidActors.Add(rb);
            }
        if (Guid.TryParse(receivable.CreatedBy, out var cg)) guidActors.Add(cg);
        if (Guid.TryParse(receivable.UpdatedBy, out var ug)) guidActors.Add(ug);

        var names = await users.GetNamesAsync(guidActors, ct);
        var events = ReceivableAuditTrailBuilder.Build(receivable, names);
        return TypedResults.Ok(events);
    }
}
