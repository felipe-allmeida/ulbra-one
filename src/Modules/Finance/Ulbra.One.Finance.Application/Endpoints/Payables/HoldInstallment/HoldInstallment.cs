using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.HoldInstallment;

public sealed class HoldInstallment : IEndpoint
{
    public sealed record Request(
        [property: SwaggerSchema("Why the installment is being held. Required — it is what tells whoever "
            + "works the payment queue why this one is parked.")]
        [property: SwaggerExample("Aguardando nota fiscal corrigida do fornecedor.")]
        string Reason);

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/installments/{installmentId}/hold", Handle)
            .WithName("HoldInstallment").WithTags("Finance")
            .WithSummary("Holds an installment out of the payment queue.")
            .WithDescription("Blocks a single installment from being paid without cancelling the payable "
                + "or touching the others. It stays visible, flagged with the reason. Holding a settled "
                + "installment returns 409.").RequirePermission(Permissions.PayablesWrite)
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
            return ErrorResults.NotFound("Installment not found.", ErrorCodes.Installment.NotFound);

        try
        {
            inst.Hold(req.Reason);
        }
        catch (InvalidOperationException ex)
        {
            return ErrorResults.InvalidState(ex.Message, ErrorCodes.Installment.NotOpen);
        }

        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok();
    }
}
