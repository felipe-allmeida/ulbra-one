using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Application.Messaging;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.ApprovePayable;

public sealed class ApprovePayable : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/approve", Handle).WithName("ApprovePayable").WithTags("Finance")
            .RequireAuthorization().Produces<PayableResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound).ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(PayableId id, UserContext user,
        IUnitOfWork uow, IPublishEndpoint publish, TimeProvider clock, CancellationToken ct)
    {
        var p = await uow.Set<Payable>().FirstOrDefaultAsync(x => x.Id == id.Value, ct);
        if (p is null) return TypedResults.Problem(detail: "Payable not found.", statusCode: 404, title: "Payable.NotFound");
        if (await ClassificationGate.RequiresButMissingAsync(uow, p, forPay: false, ct))
            return TypedResults.Problem(detail: "This payable must be classified before it can be approved.", statusCode: 400, title: "Payable.NotClassified");
        try { p.Approve(user.UserId, clock.GetUtcNow()); }
        catch (InvalidOperationException ex) { return TypedResults.Problem(detail: ex.Message, statusCode: 409, title: "Payable.InvalidState"); }
        if (p.Status == PayableStatus.Scheduled)
        {
            var code = p.AccountingAccountId is { } acc
                ? await uow.ReadSet<ChartAccount>().Where(c => c.Id == acc).Select(c => c.Code).FirstOrDefaultAsync(ct)
                : null;
            await publish.Publish(new PayableScheduled(p.Id, p.CompanyId, p.TotalAmount, code), ct);
        }
        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(PayableResponse.From(p));
    }
}
