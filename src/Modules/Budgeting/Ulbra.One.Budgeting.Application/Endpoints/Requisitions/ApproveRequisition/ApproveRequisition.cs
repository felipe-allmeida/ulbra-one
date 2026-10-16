using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Budgeting.Application.BudgetControl;
using Ulbra.One.Budgeting.Application.Contracts;
using Ulbra.One.Budgeting.Application.Messaging;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;

namespace Ulbra.One.Budgeting.Application.Endpoints.Requisitions.ApproveRequisition;

/// <summary>The Approver approves a requisition for payment, only within its Empresa × Unidade × Centro de
/// Custo scope (AEL-883, read live via <see cref="IUserScopeDirectory"/>, fail-closed intersection). No alçada
/// and no budget override (AEL-841): a ceiling breach is a hard 409 <c>BudgetExceeded</c>, resolved via
/// Adequação. On success it enters InPayment and the Finance event is published (Finance creates the Payable).</summary>
public sealed class ApproveRequisition : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/{id}/approve", Handle)
            .WithName("ApproveRequisition").WithTags("Budgeting")
            .WithSummary("Approves a requisition for payment.")
            .WithDescription("Final approval: the requisition enters InPayment and Finance is notified to "
                + "create the Payable. The approver may only act within their Company x Unit x Cost Center "
                + "scope, read live and fail-closed, so an out-of-scope requisition returns 403. There is no "
                + "authorization limit and no budget override — a ceiling breach is a hard 409, resolved by "
                + "a budget adjustment (adequação) rather than by overriding it here.")
            .RequirePermission(Permissions.RequisitionsApprove)
            .Produces<RequisitionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

    private static async Task<IResult> Handle(
        RequisitionId id, UserContext user, IUserScopeDirectory userScope, IBudgetControl budgetControl,
        IUnitOfWork uow, IPublishEndpoint publish, TimeProvider clock, CancellationToken ct)
    {
        var requisition = await uow.Set<Requisition>()
            .Include(r => r.Transitions)
            .Include(r => r.Attachments) // carried onto the Payable by the Finance integration event
            .Include(r => r.Installments) // authored plan (dates/values/payment method) → the Payable installments
            .FirstOrDefaultAsync(r => r.Id == id.Value, ct);
        if (requisition is null) return TypedResults.NotFound();

        var actor = Guid.Parse(user.UserId);

        // Approver scope (AEL-883): may approve only within its Empresa × Unidade × Centro de Custo scope
        // (intersection). Read live (revocation is immediate); Admin/Presidency bypass; empty scope approves nothing.
        if (!user.IsAdmin && !user.IsPresident)
        {
            var scope = await userScope.GetAsync(actor, ct);
            if (!scope.Covers(requisition.CompanyId, requisition.UnitId, requisition.CostCenterId))
                return ErrorResults.Forbidden("You can only approve requisitions within your scope.", ErrorCodes.Requisition.ScopeForbidden);
        }

        // Authoritative budget check — a hard block with no override. On breach the domain throws
        // BudgetExceeded (→ 409), guiding the user to raise the ceiling via an Adequação Orçamentária. The domain
        // applies the Conta Fixa exemption itself (Requisition.BypassesBudgetCeiling, AEL-881) — we only feed it
        // the raw candidate-exceeds calculation (it needs the budget read model / DB).
        var eval = await budgetControl.EvaluateCandidateAsync(requisition, ct);

        try { requisition.Approve(actor, clock.GetUtcNow(), eval.CandidateExceeds); }
        catch (RequisitionException ex) { return ErrorResults.InvalidState(ex.Error.Description, ex.Error.Code); }

        await uow.AddAsync(requisition.Transitions[^1], ct); // explicit Added (see SubmitRequisition note)
        await requisition.PublishLastTransition(publish, ct); // notify the requester (Notifications module)
        // Approved → InPayment: publish the Finance integration event on the EF bus outbox (atomic with the
        // save). Finance creates the Payable from it.
        await requisition.PublishIfApprovedForPayment(publish, ct);
        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(RequisitionResponse.From(requisition));
    }
}
