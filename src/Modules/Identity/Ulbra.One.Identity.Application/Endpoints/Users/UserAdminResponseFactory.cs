using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Identity.Application.Contracts;
using Ulbra.One.Identity.Domain;
using Ulbra.One.Identity.Domain.Entities;

namespace Ulbra.One.Identity.Application.Endpoints.Users;

/// <summary>Builds a <see cref="UserAdminResponse"/> for a single user, loading its unified scope (companies,
/// units and cost centers). Shared by the user mutation endpoints so the response shape stays consistent
/// across construction sites (AEL-883).</summary>
internal static class UserAdminResponseFactory
{
    public static async Task<UserAdminResponse> BuildAsync(IUnitOfWork uow, User user, CancellationToken ct)
    {
        var companyIds = await uow.ReadSet<UserScopeGrant>()
            .Where(g => g.UserId == user.Id).Select(g => g.CompanyId).ToListAsync(ct);
        var units = await uow.ReadSet<UserUnitScopeGrant>()
            .Where(g => g.UserId == user.Id).Select(g => new { g.CompanyId, g.UnitId }).ToListAsync(ct);
        var costCenters = await uow.ReadSet<UserCostCenterScopeGrant>()
            .Where(g => g.UserId == user.Id).Select(g => new { g.CompanyId, g.CostCenterId }).ToListAsync(ct);

        return new UserAdminResponse(
            new UserId(user.Id),
            user.Email,
            user.Name,
            user.Role,
            user.Role == Roles.Admin,
            user.Active,
            user.LastLoginAt is null,
            user.CanApproveBudgetAdjustment,
            companyIds.Select(c => new CompanyId(c)).ToArray(),
            units.Select(u => new UnitScopeAssignment(new CompanyId(u.CompanyId), new UnitId(u.UnitId))).ToArray(),
            costCenters.Select(c => new CostCenterScopeAssignment(new CompanyId(c.CompanyId), new CostCenterId(c.CostCenterId))).ToArray());
    }
}
