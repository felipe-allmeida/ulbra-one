using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.GetPayable;

public sealed class GetPayable : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/{id}", Handle).WithName("GetPayable").WithTags("Finance")
            .RequireAuthorization().Produces<PayableResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(PayableId id, IUnitOfWork uow, IUserDirectory users, CancellationToken ct)
    {
        var p = await uow.ReadSet<Payable>().Include(x => x.Attachments).FirstOrDefaultAsync(x => x.Id == id.Value, ct);
        if (p is null) return TypedResults.Problem(detail: "Payable not found.", statusCode: 404, title: "Payable.NotFound");

        var actors = new List<Guid>();
        if (Guid.TryParse(p.ApprovedBy, out var ag)) actors.Add(ag);
        if (Guid.TryParse(p.RejectedBy, out var rg)) actors.Add(rg);
        var names = await users.GetNamesAsync(actors, ct);

        return TypedResults.Ok(PayableResponse.From(p, names));
    }
}
