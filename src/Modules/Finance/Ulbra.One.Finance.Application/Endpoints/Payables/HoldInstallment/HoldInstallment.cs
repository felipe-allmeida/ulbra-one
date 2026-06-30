using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.HoldInstallment;

public sealed class HoldInstallment : IEndpoint
{
    public sealed record Request(string Reason);

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/installments/{installmentId}/hold", Handle)
            .WithName("HoldInstallment").WithTags("Finance").RequireAuthorization()
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(
        PayableId id, InstallmentId installmentId, Request req, IUnitOfWork uow, CancellationToken ct)
    {
        var payable = await uow.Set<Payable>()
            .Include(p => p.Installments)
            .FirstOrDefaultAsync(p => p.Id == id.Value, ct);

        var inst = payable?.FindInstallment(installmentId.Value);
        if (inst is null)
            return TypedResults.Problem(detail: "Installment not found.", statusCode: 404, title: "Installment.NotFound");

        try
        {
            inst.Hold(req.Reason);
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.Problem(detail: ex.Message, statusCode: 409, title: "Installment.NotOpen");
        }

        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok();
    }
}
