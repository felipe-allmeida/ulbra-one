using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using PermissionCatalog = Ulbra.One.Common.Application.Authentication.Permissions;

namespace Ulbra.One.Common.Application.Authentication;

public class UserContext(IHttpContextAccessor httpContextAccessor)
{
    private ClaimsPrincipal? User => httpContextAccessor.HttpContext?.User;

    public string UserId => User?.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("User is not authenticated");

    public string? UserName => User?.FindFirstValue(ClaimTypes.Name);
    public string? UserEmail => User?.FindFirstValue(ClaimTypes.Email);
    public string? Role => User?.FindFirstValue(ClaimTypes.Role);

    public bool IsAdmin => Role == "Admin";

    /// <summary>Presidency (AEL-883): does almost everything Admin does — permission-gated (so <c>admin:users</c>
    /// can be withheld) but, like Admin, bypasses the Empresa × Unidade × Centro de Custo scope (sees all).
    /// Literal here (like <see cref="IsAdmin"/>) so Common doesn't couple to Identity.Domain. Used only at scope
    /// bypass sites — NEVER in <see cref="HasPermission"/>, which stays claim-based so it can't reach admin:users.</summary>
    public bool IsPresident => Role == "Presidency";

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;

    /// <summary>Granted permissions (JWT <c>permission</c> claims). Admin need not list them — it bypasses.</summary>
    public IReadOnlyCollection<string> Permissions =>
        User?.FindAll(PermissionCatalog.ClaimType).Select(c => c.Value).ToArray() ?? [];

    /// <summary>True when the user is Admin (bypass) or holds the given permission.</summary>
    public bool HasPermission(string permission) =>
        IsAdmin || (User?.HasClaim(PermissionCatalog.ClaimType, permission) ?? false);

    /// <summary>Scopes (companies) granted to the user — JWT <c>scope</c> claims.</summary>
    public IReadOnlyCollection<UserScope> Scopes =>
        User?.FindAll(ScopeClaim.ClaimType)
            .Select(c => ScopeClaim.TryParse(c.Value, out var s) ? (UserScope?)s : null)
            .Where(s => s is not null)
            .Select(s => s!.Value)
            .ToArray() ?? [];

    /// <summary>Management roles with the consolidated cross-company view (AEL-906). Literals so Common doesn't
    /// couple to Identity.Domain.</summary>
    public bool CanSeeConsolidated => IsAdmin || IsPresident || Role is "Controllership" or "Finance" or "Accounting";

    /// <summary>Per-company authorization (AEL-764). Grants are an opt-in filter: an ungranted management role
    /// sees every company, a grant restricts it, scoped roles + User are fail-closed. Keyed on CanSeeConsolidated
    /// (not !IsScopedRole) so User can't fail open.</summary>
    public bool CanAccess(Guid companyId) =>
        IsAdmin || IsPresident
        || Scopes.Any(s => s.CompanyId == companyId)
        || (CanSeeConsolidated && Scopes.Count == 0);

    /// <summary>Unit scopes (company:unit) granted to the user — JWT <c>uscope</c> claims (AEL-883).</summary>
    public IReadOnlyCollection<UserUnitScope> UnitScopes =>
        User?.FindAll(UnitScopeClaim.ClaimType)
            .Select(c => UnitScopeClaim.TryParse(c.Value, out var s) ? (UserUnitScope?)s : null)
            .Where(s => s is not null)
            .Select(s => s!.Value)
            .ToArray() ?? [];

    /// <summary>Cost-center scopes (company:cost center) granted to the user — JWT <c>cscope</c> claims (AEL-883).</summary>
    public IReadOnlyCollection<UserCostCenterScope> CostCenterScopes =>
        User?.FindAll(CostCenterScopeClaim.ClaimType)
            .Select(c => CostCenterScopeClaim.TryParse(c.Value, out var s) ? (UserCostCenterScope?)s : null)
            .Where(s => s is not null)
            .Select(s => s!.Value)
            .ToArray() ?? [];

    /// <summary>Roles whose actions are restricted by the Empresa × Unidade × Centro de Custo scope (AEL-883):
    /// Requester and Approver (they act only on requisitions). Every other role sees/does everything within its
    /// permissions. Kept as literals here (like <see cref="IsAdmin"/>) so Common doesn't couple to Identity.Domain.</summary>
    public bool IsScopedRole => Role is "Requester" or "Approver";

    /// <summary>The final-Approver signature (AEL-883): holds <c>requisitions:approve</c> but neither write nor triage.
    /// Its scope is read LIVE via <see cref="IUserScopeDirectory"/> (revocation is immediate), unlike the Requester (JWT).
    /// INVARIANT: valid while the Approver is the only approve-without-write/triage role; an approve+write role would
    /// fall through to the Requester branch.</summary>
    public bool IsFinalApprover =>
        !IsAdmin
        && HasPermission(PermissionCatalog.RequisitionsApprove)
        && !HasPermission(PermissionCatalog.RequisitionsTriage)
        && !HasPermission(PermissionCatalog.RequisitionsWrite);

    /// <summary>Intersection scope check for the requisition module (AEL-883). Bypass for Admin and for any
    /// non-scoped role (Controllership/Finance/Accounting/… see everything). For a scoped role (Requester/
    /// Approver): the company, the unit AND the cost center must each be explicitly granted (no "empty = all";
    /// an empty dimension blocks). Reads the JWT claims — effective on next login.</summary>
    public bool CanAccessScope(Guid companyId, Guid unitId, Guid costCenterId)
    {
        if (IsAdmin || !IsScopedRole) return true;
        return Scopes.Any(s => s.CompanyId == companyId)
            && UnitScopes.Any(s => s.CompanyId == companyId && s.UnitId == unitId)
            && CostCenterScopes.Any(s => s.CompanyId == companyId && s.CostCenterId == costCenterId);
    }

    /// <summary>Single-dimension unit check for the requisition-form pickers (AEL-883). Admin/non-scoped bypass;
    /// otherwise the (company, unit) must be explicitly granted.</summary>
    public bool IsUnitInScope(Guid companyId, Guid unitId) =>
        IsAdmin || !IsScopedRole || UnitScopes.Any(s => s.CompanyId == companyId && s.UnitId == unitId);

    /// <summary>Single-dimension cost-center check for the requisition-form pickers (AEL-883). Admin/non-scoped
    /// bypass; otherwise the (company, cost center) must be explicitly granted.</summary>
    public bool IsCostCenterInScope(Guid companyId, Guid costCenterId) =>
        IsAdmin || !IsScopedRole || CostCenterScopes.Any(s => s.CompanyId == companyId && s.CostCenterId == costCenterId);
}
