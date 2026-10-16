using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Identity.Domain.Entities;

namespace Ulbra.One.Identity.Infrastructure;

/// <summary>Reads a user's scope (companies/units/cost centers) live from the single AppDbContext via the
/// shared unit of work — the Identity-side adapter for <see cref="IUserScopeDirectory"/> (AEL-883).</summary>
public sealed class UserScopeDirectory(IUnitOfWork uow) : IUserScopeDirectory
{
    public async Task<UserScopeSnapshot> GetAsync(Guid userId, CancellationToken ct)
    {
        var companies = await uow.ReadSet<UserScopeGrant>()
            .Where(x => x.UserId == userId).Select(x => x.CompanyId).ToListAsync(ct);
        var units = await uow.ReadSet<UserUnitScopeGrant>()
            .Where(x => x.UserId == userId).Select(x => x.UnitId).ToListAsync(ct);
        var costCenters = await uow.ReadSet<UserCostCenterScopeGrant>()
            .Where(x => x.UserId == userId).Select(x => x.CostCenterId).ToListAsync(ct);
        return new UserScopeSnapshot(companies, units, costCenters);
    }
}
