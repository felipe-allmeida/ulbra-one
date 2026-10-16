using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Budgeting.Application.Contracts;
using Ulbra.One.Budgeting.Application.Messaging;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Budgeting.Application.Endpoints.Requisitions.ResubmitRequisition;

/// <summary>Requester resubmits a requisition sent back for complement — it re-enters triage.</summary>
public sealed class ResubmitRequisition : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/resubmit", Handle)
            .WithName("ResubmitRequisition").WithTags("Budgeting")
            .WithSummary("Resubmits a requisition sent back for complement.")
            .WithDescription("The requester sends a returned requisition back into the flow; it re-enters "
                + "triage. Resubmitting one that is not awaiting complement returns 409.")
            .RequirePermission(Permissions.RequisitionsWrite)
            .Produces<RequisitionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(
        RequisitionId id, UserContext user, IUnitOfWork uow, IPublishEndpoint publish, TimeProvider clock, CancellationToken ct)
    {
        var requisition = await uow.Set<Requisition>().Include(r => r.Transitions)
            .FirstOrDefaultAsync(r => r.Id == id.Value, ct);
        // Scoped requester (AEL-883): Empresa × Unidade × Centro de Custo intersection; keep the endpoint's 404-hide.
        if (requisition is null || !user.CanAccessScope(requisition.CompanyId, requisition.UnitId, requisition.CostCenterId)) return TypedResults.NotFound();

        try { requisition.Resubmit(Guid.Parse(user.UserId), clock.GetUtcNow()); }
        catch (RequisitionException ex) { return ErrorResults.InvalidState(ex.Error.Description, ex.Error.Code); }

        await uow.AddAsync(requisition.Transitions[^1], ct); // explicit Added (see SubmitRequisition note)
        await requisition.PublishLastTransition(publish, ct); // notify triagem (Notifications module)
        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(RequisitionResponse.From(requisition));
    }
}
