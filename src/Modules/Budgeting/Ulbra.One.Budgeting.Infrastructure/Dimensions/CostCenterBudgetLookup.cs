using Microsoft.EntityFrameworkCore;
using Ulbra.One.Budgeting.Application.Dimensions;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Core.Domain.Entities;

namespace Ulbra.One.Budgeting.Infrastructure.Dimensions;

/// <summary>Core-backed implementation of <see cref="ICostCenterBudgetLookup"/> — reads the shared
/// <see cref="CostCenterUnitBudget"/> cells (Budgeting.Infrastructure → Core.Domain, read-only).</summary>
public sealed class CostCenterBudgetLookup(IUnitOfWork uow) : ICostCenterBudgetLookup
{
    public async Task<IReadOnlyList<CostCenterBudgetCell>> GetCellsAsync(
        Guid costCenterId, Guid unitId, string from, string to, CancellationToken ct)
    {
        var rows = await uow.ReadSet<CostCenterUnitBudget>()
            .Where(b => b.CostCenterId == costCenterId && b.UnitId == unitId && b.Active
                        && string.Compare(b.Competency, from) >= 0 && string.Compare(b.Competency, to) <= 0)
            .Select(b => new { b.Competency, b.MonthlyLimit })
            .ToListAsync(ct);
        return rows.Select(r => new CostCenterBudgetCell(r.Competency, r.MonthlyLimit)).ToList();
    }

    public async Task<IReadOnlyList<CostCenterOrcado>> SumOrcadoByCostCenterAsync(
        Guid companyId, Guid? unitId, string competency, CancellationToken ct)
    {
        var rows = await uow.ReadSet<CostCenterUnitBudget>()
            .Where(b => b.CompanyId == companyId && b.Active && b.Competency == competency
                        && (unitId == null || b.UnitId == unitId))
            .GroupBy(b => b.CostCenterId)
            .Select(g => new { CostCenterId = g.Key, Orcado = g.Sum(x => x.MonthlyLimit) })
            .ToListAsync(ct);
        return rows.Select(r => new CostCenterOrcado(r.CostCenterId, r.Orcado)).ToList();
    }
}
