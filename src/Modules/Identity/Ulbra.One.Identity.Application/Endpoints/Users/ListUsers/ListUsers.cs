using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Identity.Application.Contracts;
using Ulbra.One.Identity.Domain;
using Ulbra.One.Identity.Domain.Entities;

namespace Ulbra.One.Identity.Application.Endpoints.Users.ListUsers;

/// <summary>Lists users with their role, Ativo flag and unified scope (companies/units/cost centers).
/// Admin-only. Feeds the access-admin screen. See AEL-764/AEL-883.</summary>
public sealed class ListUsers : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("", Handle)
            .WithName("ListUsers")
            .WithTags("Users")
            .WithSummary("Lists users with role, status and scope.")
            .WithDescription("Every user with their role, active flag and full scope — companies, units "
                + "and cost centers. Feeds the access-admin screen. A reference list: no paging and no "
                + "filters. A user pre-registered but who has never signed in is flagged pendingFirstLogin.")
            .RequirePermission(Permissions.AdminUsers)
            .Produces<List<UserAdminResponse>>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(IUnitOfWork uow, CancellationToken ct)
    {
        var users = await uow.ReadSet<User>().OrderBy(u => u.Name).ToListAsync(ct);

        var companyGrants = (await uow.ReadSet<UserScopeGrant>().ToListAsync(ct))
            .GroupBy(g => g.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => new CompanyId(x.CompanyId)).ToArray());
        var unitGrants = (await uow.ReadSet<UserUnitScopeGrant>().ToListAsync(ct))
            .GroupBy(g => g.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => new UnitScopeAssignment(new CompanyId(x.CompanyId), new UnitId(x.UnitId))).ToArray());
        var costCenterGrants = (await uow.ReadSet<UserCostCenterScopeGrant>().ToListAsync(ct))
            .GroupBy(g => g.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => new CostCenterScopeAssignment(new CompanyId(x.CompanyId), new CostCenterId(x.CostCenterId))).ToArray());

        var result = users.Select(u => new UserAdminResponse(
            new UserId(u.Id),
            u.Email,
            u.Name,
            u.PictureUrl,
            u.Role,
            u.Role == Roles.Admin,
            u.Active,
            u.LastLoginAt is null,
            u.CanApproveBudgetAdjustment,
            companyGrants.TryGetValue(u.Id, out var cids) ? cids : [],
            unitGrants.TryGetValue(u.Id, out var uids) ? uids : [],
            costCenterGrants.TryGetValue(u.Id, out var ccs) ? ccs : [])).ToList();

        return TypedResults.Ok(result);
    }
}
