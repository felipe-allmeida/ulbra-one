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

namespace Ulbra.One.Budgeting.Application.Endpoints.Requisitions.ReturnRequisitionToFinancialComplement;

/// <summary>The Approver returns a front-loaded requisition to the requester specifically to revise the
/// financial plan — Approval → FinancialComplement — without redoing triage. Only the Approver reaches it
/// (single source state, single role), so it requires the approve permission and the domain validates the source
/// state. Mirrors <c>ReturnRequisitionForComplement</c>.</summary>
public sealed class ReturnRequisitionToFinancialComplement : IEndpoint
{
    public sealed record Request(
        [property: SwaggerSchema("What the requester has to revise in the financial plan (parcelas/emissão). "
            + "Optional — it is what the transition history shows them.")]
        [property: SwaggerExample("Ajustar a data de vencimento da 2ª parcela.")]
        string? Reason);

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/return-to-financial-complement", Handle)
            .WithName("ReturnRequisitionToFinancialComplement").WithTags("Budgeting")
            .WithSummary("Returns a requisition to revise the financial plan.")
            .WithDescription("The Approver sends a front-loaded requisition back to the requester to revise "
                + "the financial part (installments / emission date) without redoing triage — Approval → "
                + "FinancialComplement. Requires the approval permission; the domain validates the source "
                + "state (409).")
            .RequirePermission(Permissions.RequisitionsApprove)
            .Produces<RequisitionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(
        RequisitionId id, Request req, UserContext user, IUserScopeDirectory userScope, IUnitOfWork uow, IPublishEndpoint publish, TimeProvider clock, CancellationToken ct)
    {
        var requisition = await uow.Set<Requisition>().Include(r => r.Transitions)
            .FirstOrDefaultAsync(r => r.Id == id.Value, ct);
        if (requisition is null) return TypedResults.NotFound();

        // Scope gate (AEL-883): the final Approver acts only within its Empresa × Unidade × Centro de Custo
        // scope (read LIVE); admin / non-scoped bypass, mirroring ReturnRequisitionForComplement. 404-hide.
        if (user.IsFinalApprover
            && !(await userScope.GetAsync(Guid.Parse(user.UserId), ct)).Covers(requisition.CompanyId, requisition.UnitId, requisition.CostCenterId))
            return TypedResults.NotFound();

        try { requisition.ReturnToFinancialComplement(Guid.Parse(user.UserId), clock.GetUtcNow(), req.Reason); }
        catch (RequisitionException ex) { return ErrorResults.InvalidState(ex.Error.Description, ex.Error.Code); }

        await uow.AddAsync(requisition.Transitions[^1], ct); // explicit Added (see SubmitRequisition note)
        await requisition.PublishLastTransition(publish, ct); // ToStatus=FinancialComplement → personal to requester
        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(RequisitionResponse.From(requisition));
    }
}
