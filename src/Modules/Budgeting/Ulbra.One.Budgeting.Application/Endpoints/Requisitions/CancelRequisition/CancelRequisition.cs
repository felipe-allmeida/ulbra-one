using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Budgeting.Application.Contracts;
using Ulbra.One.Budgeting.Application.Messaging;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;

namespace Ulbra.One.Budgeting.Application.Endpoints.Requisitions.CancelRequisition;

/// <summary>Cancels a requisition still in the pre-settlement flow. Available to the requester (write) as
/// well as Controle (triage) and the Approver (approve); the domain validates the source state.</summary>
public sealed class CancelRequisition : IEndpoint
{
    public sealed record Request(
        [property: SwaggerSchema("Why the requisition is being cancelled. Optional, but it is what the "
            + "requester sees in the transition history.")]
        [property: SwaggerExample("Purchase no longer needed for this period.")]
        string? Reason);

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/cancel", Handle)
            .WithName("CancelRequisition").WithTags("Budgeting")
            .WithSummary("Cancels a requisition.")
            .WithDescription("Cancels a requisition still in the pre-settlement flow. Open to the requester "
                + "as well as Controle and the Approver — any one of the write, triage or approve "
                + "permissions will do, hence the 403 — and the domain validates the source state (409).")
            .RequireAuthorization()
            .Produces<RequisitionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(
        RequisitionId id, Request req, UserContext user, IUserScopeDirectory userScope, IUnitOfWork uow, IPublishEndpoint publish, TimeProvider clock, CancellationToken ct)
    {
        if (!user.HasPermission(Permissions.RequisitionsWrite)
            && !user.HasPermission(Permissions.RequisitionsTriage)
            && !user.HasPermission(Permissions.RequisitionsApprove))
            return ErrorResults.Forbidden("Requires a requisition write, triage or approval permission.", ErrorCodes.Requisition.Forbidden);

        var requisition = await uow.Set<Requisition>()
            .Include(r => r.Transitions)
            .Include(r => r.Payments) // the cancel guard inspects registered payments
            .FirstOrDefaultAsync(r => r.Id == id.Value, ct);
        if (requisition is null) return TypedResults.NotFound();

        // Scope gate (AEL-883): the scoped requester (write, JWT claims) and the final Approver (approve, read
        // LIVE so revocation is immediate) may cancel only within their Empresa × Unidade × Centro de Custo
        // scope; Controle (triage) / Finance / Admin bypass. 404-hide, matching the other id-addressed gates.
        var inScope = user.IsFinalApprover
            ? (await userScope.GetAsync(Guid.Parse(user.UserId), ct)).Covers(requisition.CompanyId, requisition.UnitId, requisition.CostCenterId)
            : user.CanAccessScope(requisition.CompanyId, requisition.UnitId, requisition.CostCenterId);
        if (!inScope) return TypedResults.NotFound();

        try { requisition.Cancel(Guid.Parse(user.UserId), clock.GetUtcNow(), req.Reason); }
        catch (RequisitionException ex) { return ErrorResults.InvalidState(ex.Error.Description, ex.Error.Code); }

        await uow.AddAsync(requisition.Transitions[^1], ct); // explicit Added (see SubmitRequisition note)
        await requisition.PublishLastTransition(publish, ct); // notify the requester (Notifications module)
        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(RequisitionResponse.From(requisition));
    }
}
