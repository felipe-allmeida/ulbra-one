using Microsoft.EntityFrameworkCore;
using Ulbra.One.Accounting.Application.CostCenters;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Core.Domain.Entities;

namespace Ulbra.One.Accounting.Infrastructure.CostCenters;

/// <summary>Reads Core cost centers from the shared context to validate journal-line cost centers.
/// The dependency on Core.Domain lives here in Infrastructure, mirroring the module's event-contract
/// reference to Finance — the Application layer stays Core-free behind <see cref="ICostCenterLookup"/>.</summary>
public sealed class CostCenterLookup(IUnitOfWork uow) : ICostCenterLookup
{
    public async Task<IReadOnlyDictionary<Guid, CostCenterInfo>> GetAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return new Dictionary<Guid, CostCenterInfo>();
        var rows = await uow.ReadSet<CostCenter>()
            .Where(c => ids.Contains(c.Id))
            .Select(c => new CostCenterInfo(c.Id, c.CompanyId, c.Active, c.Code, c.Name))
            .ToListAsync(ct);
        return rows.ToDictionary(c => c.Id);
    }
}
