using Microsoft.EntityFrameworkCore;
using Ulbra.One.Accounting.Application.Units;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Core.Domain.Entities;

namespace Ulbra.One.Accounting.Infrastructure.Units;

/// <summary>Reads Core units from the shared context to validate journal-line units. The dependency on
/// Core.Domain lives here in Infrastructure, mirroring <see cref="CostCenters.CostCenterLookup"/> — the
/// Application layer stays Core-free behind <see cref="IUnitLookup"/>.</summary>
public sealed class UnitLookup(IUnitOfWork uow) : IUnitLookup
{
    public async Task<IReadOnlyDictionary<Guid, UnitInfo>> GetAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return new Dictionary<Guid, UnitInfo>();
        var rows = await uow.ReadSet<Unit>()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new UnitInfo(u.Id, u.CompanyId, u.Active, u.Name, u.Acronym))
            .ToListAsync(ct);
        return rows.ToDictionary(u => u.Id);
    }
}
