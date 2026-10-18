using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Core.Domain.Entities;

namespace Ulbra.One.Core.Application.Endpoints.CostCenters;

/// <summary>Shared cost-center ↔ unit validation used by create/update slices.</summary>
internal static class UnitReconciliation
{
    /// <summary>True when every requested unit belongs to the given company (empty set is valid).</summary>
    public static async Task<bool> UnitsValid(IUnitOfWork uow, Guid companyId, Guid[] unitIds, CancellationToken ct)
    {
        if (unitIds.Length == 0) return true;
        var valid = await uow.ReadSet<Unit>()
            .Where(u => u.CompanyId == companyId && unitIds.Contains(u.Id))
            .Select(u => u.Id).ToListAsync(ct);
        return valid.Count == unitIds.Length;
    }
}
