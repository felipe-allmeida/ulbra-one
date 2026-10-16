using Microsoft.EntityFrameworkCore;
using Ulbra.One.Budgeting.Application.Dimensions;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Core.Domain.Entities;

namespace Ulbra.One.Budgeting.Infrastructure.Dimensions;

/// <summary>Core-backed implementation of <see cref="ICostCenterBudgetMutator"/>. A deliberate cross-module
/// reference (Budgeting.Infrastructure → Core.Domain), mutating the shared <see cref="CostCenterUnitBudget"/>
/// cells on the same unit of work as the adjustment's status change.</summary>
public sealed class CostCenterBudgetMutator(IUnitOfWork uow) : ICostCenterBudgetMutator
{
    public async Task<BudgetMutationStatus> IncreaseOrCreateAsync(
        Guid costCenterId, Guid unitId, Guid companyId, string competency, decimal value, CancellationToken ct)
    {
        var cell = await LoadAsync(costCenterId, unitId, competency, ct);
        if (cell is null)
            await uow.AddAsync(new CostCenterUnitBudget(costCenterId, unitId, companyId, competency, value), ct);
        else
            cell.Increase(value);
        return BudgetMutationStatus.Ok;
    }

    public async Task<BudgetMutationStatus> DecreaseExistingAsync(
        Guid costCenterId, Guid unitId, string competency, decimal value, CancellationToken ct)
    {
        var cell = await LoadAsync(costCenterId, unitId, competency, ct);
        if (cell is null) return BudgetMutationStatus.CellNotFound;
        // Pre-check so the caller gets a clean status instead of the entity's defensive exception.
        if (decimal.Round(cell.MonthlyLimit - value, 2, MidpointRounding.AwayFromZero) < 0)
            return BudgetMutationStatus.WouldGoNegative;
        cell.Decrease(value);
        return BudgetMutationStatus.Ok;
    }

    private Task<CostCenterUnitBudget?> LoadAsync(Guid costCenterId, Guid unitId, string competency, CancellationToken ct) =>
        uow.Set<CostCenterUnitBudget>().FirstOrDefaultAsync(
            b => b.CostCenterId == costCenterId && b.UnitId == unitId && b.Competency == competency, ct);
}
