using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Budgeting.Application.BudgetControl;
using Ulbra.One.Budgeting.Application.Contracts;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Budgeting.Application.Endpoints.Requisitions.BudgetControl;

/// <summary>Server-computed budget panel for an existing requisition — the per-month ledger over its window
/// plus the candidate verdict (breaches / unbudgeted / exceeds). Replaces the interim client-side sum used by
/// the detail/approve panel.</summary>
public sealed class GetRequisitionBudgetControl : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("/{id}/budget-control", Handle)
            .WithName("GetRequisitionBudgetControl").WithTags("Budgeting")
            .WithSummary("Describes the budget impact of a requisition.")
            .WithDescription("Per-month budget ledger over an existing requisition's window plus the "
                + "candidate verdict (breaches, unbudgeted, exceeds). Each month carries the requisition's "
                + "exact cost share — the remainder falls on the last month — so the detail and approval "
                + "panels never have to approximate.")
            .RequirePermission(Permissions.RequisitionsRead)
            .Produces<BudgetControlResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(
        RequisitionId id, IBudgetControl budgetControl, IUnitOfWork uow, CancellationToken ct)
    {
        var requisition = await uow.ReadSet<Requisition>().FirstOrDefaultAsync(r => r.Id == id.Value, ct);
        if (requisition is null) return TypedResults.NotFound();

        var evaluation = await budgetControl.EvaluateCandidateAsync(requisition, ct);
        // Custo exato do pedido por competência (rateio resto-na-última) em cada mês, p/ o painel não aproximar.
        return TypedResults.Ok(BudgetControlResponse.From(evaluation, requisition.EstimatedContributionFor));
    }
}
