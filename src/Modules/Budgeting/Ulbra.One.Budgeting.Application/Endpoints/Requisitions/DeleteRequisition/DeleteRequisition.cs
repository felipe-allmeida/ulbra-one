using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Budgeting.Application.Endpoints.Requisitions.DeleteRequisition;

/// <summary>Hard-deletes a Draft requisition (discarding an unsent draft). Submitted requisitions are
/// never deleted — they are cancelled (a transition, in a later issue) — so a non-Draft returns 409.
/// Hard delete mirrors the module's Budget (disposable, no audit trail while Draft).</summary>
public sealed class DeleteRequisition : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapDelete("/{id}", Handle)
            .WithName("DeleteRequisition")
            .WithTags("Budgeting")
            .WithSummary("Deletes a draft requisition.")
            .WithDescription("Hard-deletes a Draft — discarding a requisition that was never submitted, so "
                + "there is no audit trail to preserve. A submitted requisition is cancelled through a "
                + "transition rather than deleted, so any other state returns 409.")
            .RequirePermission(Permissions.RequisitionsWrite)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> Handle(
        RequisitionId id,
        UserContext user,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var requisition = await uow.Set<Requisition>().FirstOrDefaultAsync(r => r.Id == id.Value, ct);
        // Scoped requester (AEL-883): Empresa × Unidade × Centro de Custo intersection; 404-hide, and it also
        // enforces the company dimension that this endpoint previously left open.
        if (requisition is null || !user.CanAccessScope(requisition.CompanyId, requisition.UnitId, requisition.CostCenterId)) return TypedResults.NotFound();

        try
        {
            requisition.EnsureDeletable();
        }
        catch (RequisitionException ex)
        {
            return ErrorResults.InvalidState(ex.Error.Description, ex.Error.Code);
        }

        uow.Remove(requisition);
        await uow.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }
}
