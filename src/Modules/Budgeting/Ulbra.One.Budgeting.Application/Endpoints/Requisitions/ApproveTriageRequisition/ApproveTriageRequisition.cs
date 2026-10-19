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

namespace Ulbra.One.Budgeting.Application.Endpoints.Requisitions.ApproveTriageRequisition;

/// <summary>Controladoria approves the triage (vendor/cost center/unit vetted) and either returns the requisition
/// to the requester for the financial complement or — when the requester front-loaded the plan — sends
/// it straight to approval. The domain snapshots the vetted value/vendor and decides the route.</summary>
public sealed class ApproveTriageRequisition : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/approve-triage", Handle)
            .WithName("ApproveTriageRequisition").WithTags("Budgeting")
            .WithSummary("Approves a requisition's triage.")
            .WithDescription("Controladoria confirms the vendor, cost center and unit are vetted. When the "
                + "financial part is still pending, the requisition goes back to the requester for the "
                + "financial complement; when it was front-loaded already, it goes straight to approval. The "
                + "vetted value and vendor are snapshotted here, so a later complement that raises the total "
                + "or swaps the vendor returns to triage instead of going straight to approval.")
            .RequirePermission(Permissions.RequisitionsTriage)
            .Produces<RequisitionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(
        RequisitionId id, UserContext user, IUnitOfWork uow, IPublishEndpoint publish, TimeProvider clock, CancellationToken ct)
    {
        // Include the installments: ApproveTriage reads them to decide whether a front-loaded plan lets it skip
        // the financial-complement stage. Without the Include the collection loads empty → always routes
        // to FinancialComplement.
        var requisition = await uow.Set<Requisition>()
            .Include(r => r.Transitions)
            .Include(r => r.Installments)
            .FirstOrDefaultAsync(r => r.Id == id.Value, ct);
        if (requisition is null) return TypedResults.NotFound();

        try { requisition.ApproveTriage(Guid.Parse(user.UserId), clock.GetUtcNow()); }
        catch (RequisitionException ex) { return ErrorResults.InvalidState(ex.Error.Description, ex.Error.Code); }

        await uow.AddAsync(requisition.Transitions[^1], ct); // explicit Added (see SubmitRequisition note)
        await requisition.PublishLastTransition(publish, ct); // notify the requester the financial complement is due
        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(RequisitionResponse.From(requisition));
    }
}
