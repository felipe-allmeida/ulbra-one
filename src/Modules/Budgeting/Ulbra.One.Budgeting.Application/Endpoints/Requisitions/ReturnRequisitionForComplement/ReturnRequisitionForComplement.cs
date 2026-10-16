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

namespace Ulbra.One.Budgeting.Application.Endpoints.Requisitions.ReturnRequisitionForComplement;

/// <summary>Sends a requisition back to the requester for complement. Available to both Controle (from
/// triage) and the Approver (from approval), so the endpoint accepts either permission and lets the domain
/// validate the source state.</summary>
public sealed class ReturnRequisitionForComplement : IEndpoint
{
    public sealed record Request(
        [property: SwaggerSchema("What the requester has to fix or supply. Optional, but it is the only "
            + "guidance they get — it is what the transition history shows them.")]
        [property: SwaggerExample("Attach the vendor's quote and confirm the delivery address.")]
        string? Reason);

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/return-for-complement", Handle)
            .WithName("ReturnRequisitionForComplement").WithTags("Budgeting")
            .WithSummary("Sends a requisition back for complement.")
            .WithDescription("Returns a requisition to the requester for more information. Reachable from "
                + "both triage (Controle) and approval (the Approver), so either permission is accepted — "
                + "hence the 403 — and the domain validates the source state (409).")
            .RequireAuthorization()
            .Produces<RequisitionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(
        RequisitionId id, Request req, UserContext user, IUserScopeDirectory userScope, IUnitOfWork uow, IPublishEndpoint publish, TimeProvider clock, CancellationToken ct)
    {
        if (!user.HasPermission(Permissions.RequisitionsTriage) && !user.HasPermission(Permissions.RequisitionsApprove))
            return ErrorResults.Forbidden("Requires the triage or approval permission.", ErrorCodes.Requisition.Forbidden);

        var requisition = await uow.Set<Requisition>().Include(r => r.Transitions)
            .FirstOrDefaultAsync(r => r.Id == id.Value, ct);
        if (requisition is null) return TypedResults.NotFound();

        // Scope gate (AEL-883): the final Approver (approve, read LIVE) returns only within its Empresa ×
        // Unidade × Centro de Custo scope; Controle (triage) bypasses (non-scoped role). 404-hide.
        if (user.IsFinalApprover
            && !(await userScope.GetAsync(Guid.Parse(user.UserId), ct)).Covers(requisition.CompanyId, requisition.UnitId, requisition.CostCenterId))
            return TypedResults.NotFound();

        try { requisition.ReturnForComplement(Guid.Parse(user.UserId), clock.GetUtcNow(), req.Reason); }
        catch (RequisitionException ex) { return ErrorResults.InvalidState(ex.Error.Description, ex.Error.Code); }

        await uow.AddAsync(requisition.Transitions[^1], ct); // explicit Added (see SubmitRequisition note)
        await requisition.PublishLastTransition(publish, ct); // notify the requester (Notifications module)
        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(RequisitionResponse.From(requisition));
    }
}
