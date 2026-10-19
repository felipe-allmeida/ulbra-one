using Ulbra.One.Budgeting.Application.Dimensions;
using Ulbra.One.Budgeting.Domain;
using Ulbra.One.Budgeting.Domain.Entities;

namespace Ulbra.One.Budgeting.Application.Endpoints.BudgetAdjustments;

/// <summary>Applies (on approval) or reverses (on reversal) the ceiling change of a budget adjustment to the
/// affected Core budget cell(s) via <see cref="ICostCenterBudgetMutator"/>, in the SAME unit of work as the
/// adjustment's status change (so both commit atomically). Suplementação/Transfer create the destination cell
/// when absent; Redução and the transfer source require an existing cell
/// (<see cref="BudgetAdjustmentErrors.CellNotFound"/>) and never go below zero
/// (<see cref="BudgetAdjustmentErrors.LimitWouldGoNegative"/>).</summary>
internal static class BudgetAdjustmentMutation
{
    /// <summary>Applies the approved adjustment to the budget.</summary>
    public static async Task ApplyAsync(ICostCenterBudgetMutator mutator, BudgetAdjustment a, CancellationToken ct)
    {
        switch (a.Type)
        {
            case BudgetAdjustmentType.Supplementation:
                await IncreaseOrCreate(mutator, a, a.Competency, ct);
                break;
            case BudgetAdjustmentType.Reduction:
                await DecreaseExisting(mutator, a, a.Competency, ct);
                break;
            case BudgetAdjustmentType.Transfer:
                await DecreaseExisting(mutator, a, a.SourceCompetency!, ct); // out of the source month
                await IncreaseOrCreate(mutator, a, a.Competency, ct);        // into the destination month
                break;
        }
    }

    /// <summary>Reverses a previously-applied adjustment (inverse arithmetic — recomposes the budget).</summary>
    public static async Task ReverseAsync(ICostCenterBudgetMutator mutator, BudgetAdjustment a, CancellationToken ct)
    {
        switch (a.Type)
        {
            case BudgetAdjustmentType.Supplementation:
                await DecreaseExisting(mutator, a, a.Competency, ct);
                break;
            case BudgetAdjustmentType.Reduction:
                await IncreaseOrCreate(mutator, a, a.Competency, ct);
                break;
            case BudgetAdjustmentType.Transfer:
                await IncreaseOrCreate(mutator, a, a.SourceCompetency!, ct); // give the source month back
                await DecreaseExisting(mutator, a, a.Competency, ct);        // take it out of the destination
                break;
        }
    }

    private static async Task IncreaseOrCreate(ICostCenterBudgetMutator mutator, BudgetAdjustment a, string competency, CancellationToken ct) =>
        Ensure(await mutator.IncreaseOrCreateAsync(a.CostCenterId, a.UnitId, a.CompanyId, competency, a.Value, ct));

    private static async Task DecreaseExisting(ICostCenterBudgetMutator mutator, BudgetAdjustment a, string competency, CancellationToken ct) =>
        Ensure(await mutator.DecreaseExistingAsync(a.CostCenterId, a.UnitId, competency, a.Value, ct));

    /// <summary>Maps the mutator's status to the stable adjustment error codes (the endpoints catch these and
    /// map to 409), keeping the i18n contract identical to the former in-place mutation.</summary>
    private static void Ensure(BudgetMutationStatus status)
    {
        switch (status)
        {
            case BudgetMutationStatus.CellNotFound:
                throw new BudgetAdjustmentException(BudgetAdjustmentErrors.CellNotFound);
            case BudgetMutationStatus.WouldGoNegative:
                throw new BudgetAdjustmentException(BudgetAdjustmentErrors.LimitWouldGoNegative);
        }
    }
}
