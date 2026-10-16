using Microsoft.EntityFrameworkCore;
using Ulbra.One.Budgeting.Application.Dimensions;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Core.Domain.Entities;

namespace Ulbra.One.Budgeting.Infrastructure.Dimensions;

/// <summary>Core-backed implementation of <see cref="IBudgetDimensionLookup"/>. The single deliberate
/// cross-module type reference (Budgeting.Infrastructure → Core.Domain); read-only, no FK.</summary>
public sealed class BudgetDimensionLookup(IUnitOfWork uow) : IBudgetDimensionLookup
{
    public async Task<DimensionResult> ResolveAsync(Guid unitId, Guid costCenterId, CancellationToken ct)
    {
        var unit = await uow.ReadSet<Unit>().FirstOrDefaultAsync(u => u.Id == unitId, ct);
        if (unit is null) return new DimensionResult(DimensionStatus.UnitNotFound, Guid.Empty);

        var costCenter = await uow.ReadSet<CostCenter>().FirstOrDefaultAsync(c => c.Id == costCenterId, ct);
        if (costCenter is null) return new DimensionResult(DimensionStatus.CostCenterNotFound, Guid.Empty);
        if (costCenter.CompanyId != unit.CompanyId) return new DimensionResult(DimensionStatus.CompanyMismatch, Guid.Empty);
        if (!costCenter.Active) return new DimensionResult(DimensionStatus.CostCenterInactive, Guid.Empty);

        var associated = await uow.ReadSet<CostCenterUnit>()
            .AnyAsync(x => x.CostCenterId == costCenterId && x.UnitId == unitId, ct);
        if (!associated) return new DimensionResult(DimensionStatus.UnitNotInCostCenter, Guid.Empty);

        return new DimensionResult(DimensionStatus.Ok, unit.CompanyId);
    }

    public async Task<DimensionResult> ResolveUnitAsync(Guid unitId, CancellationToken ct)
    {
        var unit = await uow.ReadSet<Unit>().FirstOrDefaultAsync(u => u.Id == unitId, ct);
        return unit is null
            ? new DimensionResult(DimensionStatus.UnitNotFound, Guid.Empty)
            : new DimensionResult(DimensionStatus.Ok, unit.CompanyId);
    }

    public async Task<IReadOnlyList<CostCenterInfo>> ListActiveCostCentersAsync(Guid companyId, CancellationToken ct) =>
        await uow.ReadSet<CostCenter>()
            .Where(c => c.CompanyId == companyId && c.Active)
            .OrderBy(c => c.Name)
            .Select(c => new CostCenterInfo(c.Id, c.Name))
            .ToListAsync(ct);
}
