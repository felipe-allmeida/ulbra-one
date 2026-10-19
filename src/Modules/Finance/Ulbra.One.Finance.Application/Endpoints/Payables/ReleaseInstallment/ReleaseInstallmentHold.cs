using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.ReleaseInstallment;

public sealed class ReleaseInstallmentHold : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/installments/{installmentId}/release-hold", Handle)
            .WithName("ReleaseInstallmentHold").WithTags("Finance")
            .WithSummary("Releases an installment hold.")
            .WithDescription("Puts a held installment back in the payment queue. Idempotent — releasing "
                + "one that is not held succeeds and changes nothing.").RequirePermission(Permissions.PayablesWrite)
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(
        PayableId id, InstallmentId installmentId, IUnitOfWork uow, CancellationToken ct)
    {
        var payable = await uow.Set<Payable>()
            .Include(p => p.Installments)
            .FirstOrDefaultAsync(p => p.Id == id.Value, ct);

        var inst = payable?.FindInstallment(installmentId.Value);
        if (inst is null)
            return ErrorResults.NotFound("Installment not found.", ErrorCodes.Installment.NotFound);

        inst.ReleaseHold();
        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok();
    }
}
