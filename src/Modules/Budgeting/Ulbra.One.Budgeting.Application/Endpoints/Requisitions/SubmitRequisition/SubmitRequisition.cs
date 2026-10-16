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

namespace Ulbra.One.Budgeting.Application.Endpoints.Requisitions.SubmitRequisition;

/// <summary>Solicitante submits a Draft — it always enters Triage. There are no shortcuts (no requester
/// autonomy, no cost-center auto-approval); every requisition is triaged, complemented and approved (AEL-841).</summary>
public sealed class SubmitRequisition : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/submit", Handle)
            .WithName("SubmitRequisition").WithTags("Budgeting")
            .WithSummary("Submits a draft requisition.")
            .WithDescription("Moves a Draft into Triage. There are no shortcuts — no requester autonomy and "
                + "no cost-center auto-approval — so every requisition is triaged, complemented and approved. "
                + "Submitting anything that is not a Draft returns 409.")
            .RequirePermission(Permissions.RequisitionsWrite)
            .Produces<RequisitionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(
        RequisitionId id, UserContext user, IUnitOfWork uow, IPublishEndpoint publish, TimeProvider clock, CancellationToken ct)
    {
        // Include the installments — Submit guards each boleto line; without it the collection loads empty and
        // the 47/48 rule never fires.
        var requisition = await uow.Set<Requisition>()
            .Include(r => r.Transitions)
            .Include(r => r.Installments)
            .FirstOrDefaultAsync(r => r.Id == id.Value, ct);
        // Scoped requester (AEL-883): Empresa × Unidade × Centro de Custo intersection; keep the endpoint's 404-hide.
        if (requisition is null || !user.CanAccessScope(requisition.CompanyId, requisition.UnitId, requisition.CostCenterId)) return TypedResults.NotFound();

        try { requisition.Submit(Guid.Parse(user.UserId), clock.GetUtcNow()); }
        catch (RequisitionException ex) { return ErrorResults.InvalidState(ex.Error.Description, ex.Error.Code); }

        // Persist the new history row explicitly as Added — EF's store-generated-key graph heuristic would
        // otherwise mark a set-key child appended to a Modified parent as Modified (0-row UPDATE). Same as
        // PayablePaymentService adding a Payment. Existing (loaded) transitions stay Unchanged.
        await uow.AddAsync(requisition.Transitions[^1], ct);
        await requisition.PublishLastTransition(publish, ct); // notify triagem (Notifications module)
        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(RequisitionResponse.From(requisition));
    }
}
