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

namespace Ulbra.One.Budgeting.Application.Endpoints.Requisitions.TerminateRecurrence;

/// <summary>Terminates a recurring requisition (AEL-877, "Encerrar Conta Fixa"): fixes the end competency so the
/// requisition can finalize. Stays InPayment; if every cycle (base + launched) is already covered it settles to
/// Paid right away. No new Payable is generated.</summary>
public sealed class TerminateRecurrenceRequisition : IEndpoint
{
    public sealed record Request(
        [property: SwaggerSchema("Last competency the recurrence covers (`YYYY-MM`). Fixing this end date "
            + "is what lets the requisition finalize.")]
        [property: SwaggerExample("2026-12")]
        string EndCompetency);

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/terminate-recurrence", Handle)
            .WithName("TerminateRecurrence").WithTags("Budgeting")
            .WithSummary("Ends a recurring requisition.")
            .WithDescription("Closes a recurring requisition (\"Encerrar Conta Fixa\") by fixing its end "
                + "competency, so it can finalize. It stays InPayment, and settles to Paid immediately if "
                + "every cycle — the base one plus each launched invoice — is already covered. No new "
                + "Payable is generated.")
            .RequirePermission(Permissions.RequisitionsWrite)
            .Produces<RequisitionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(
        RequisitionId id, Request req, UserContext user, IUnitOfWork uow, IPublishEndpoint publish, TimeProvider clock, CancellationToken ct)
    {
        var requisition = await uow.Set<Requisition>()
            .Include(r => r.Transitions)
            .Include(r => r.Cycles)
            .Include(r => r.Payments) // AllCyclesSettled sums the realizado
            .FirstOrDefaultAsync(r => r.Id == id.Value, ct);
        // Scoped requester (AEL-883): Empresa × Unidade × Centro de Custo intersection; keep the endpoint's 404-hide.
        if (requisition is null || !user.CanAccessScope(requisition.CompanyId, requisition.UnitId, requisition.CostCenterId)) return TypedResults.NotFound();

        var transitionsBefore = requisition.Transitions.Count;
        try { requisition.TerminateRecurrence(req.EndCompetency, Guid.Parse(user.UserId), clock.GetUtcNow()); }
        catch (RequisitionException ex) { return ErrorResults.InvalidState(ex.Error.Description, ex.Error.Code); }

        // TerminateRecurrence appends a trail entry (and MarkPaid appends another if it settles on the spot).
        for (var i = transitionsBefore; i < requisition.Transitions.Count; i++)
            await uow.AddAsync(requisition.Transitions[i], ct);
        await requisition.PublishLastTransition(publish, ct); // notify (Notifications)
        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(RequisitionResponse.From(requisition));
    }
}
