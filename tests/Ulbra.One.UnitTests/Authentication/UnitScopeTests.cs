using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Identity.Domain;
using Xunit;

namespace Ulbra.One.UnitTests.Authentication;

/// <summary>Unified intersection scope authorization core (AEL-883, iteração 2): the <c>uscope</c>/<c>cscope</c>
/// claim round-trips, the <see cref="UserContext.CanAccessScope"/> rule (Empresa × Unidade × Centro de Custo,
/// explicit lists — an empty dimension BLOCKS), restricted to the scoped roles (Requester/Approver), plus the
/// live <see cref="UserScopeSnapshot.Covers"/> intersection used where revocation must be immediate.</summary>
public class UnitScopeTests
{
    private static UserContext ContextWith(
        string role,
        Guid[] companies,
        (Guid Company, Guid Unit)[] units,
        (Guid Company, Guid CostCenter)[] costCenters)
    {
        var claims = new List<Claim> { new(ClaimTypes.Role, role) };
        foreach (var c in companies) claims.Add(new Claim(ScopeClaim.ClaimType, ScopeClaim.Encode(c)));
        foreach (var (company, unit) in units) claims.Add(new Claim(UnitScopeClaim.ClaimType, UnitScopeClaim.Encode(company, unit)));
        foreach (var (company, cc) in costCenters) claims.Add(new Claim(CostCenterScopeClaim.ClaimType, CostCenterScopeClaim.Encode(company, cc)));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } };
        return new UserContext(accessor);
    }

    private static UserContext ContextWithPermissions(string role, params string[] permissions)
    {
        var claims = new List<Claim> { new(ClaimTypes.Role, role) };
        foreach (var p in permissions) claims.Add(new Claim(Permissions.ClaimType, p));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } };
        return new UserContext(accessor);
    }

    // ---- Claim round-trips -----------------------------------------------------------------------

    [Fact]
    public void UnitScopeClaim_round_trips()
    {
        var company = Guid.NewGuid();
        var unit = Guid.NewGuid();

        Assert.True(UnitScopeClaim.TryParse(UnitScopeClaim.Encode(company, unit), out var scope));
        Assert.Equal(company, scope.CompanyId);
        Assert.Equal(unit, scope.UnitId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-scope")]
    [InlineData("only-one-guid")]
    [InlineData(":")]
    public void UnitScopeClaim_rejects_malformed(string value)
    {
        Assert.False(UnitScopeClaim.TryParse(value, out _));
    }

    [Fact]
    public void CostCenterScopeClaim_round_trips()
    {
        var company = Guid.NewGuid();
        var costCenter = Guid.NewGuid();

        Assert.True(CostCenterScopeClaim.TryParse(CostCenterScopeClaim.Encode(company, costCenter), out var scope));
        Assert.Equal(company, scope.CompanyId);
        Assert.Equal(costCenter, scope.CostCenterId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-scope")]
    [InlineData("only-one-guid")]
    [InlineData(":")]
    public void CostCenterScopeClaim_rejects_malformed(string value)
    {
        Assert.False(CostCenterScopeClaim.TryParse(value, out _));
    }

    // ---- CanAccessScope: bypasses ----------------------------------------------------------------

    [Fact]
    public void Admin_bypasses_the_scope_intersection()
    {
        // Admin holds no scope claims yet accesses any (company, unit, cost center).
        var user = ContextWith("Admin", [], [], []);
        Assert.True(user.CanAccessScope(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public void Non_scoped_role_bypasses_the_scope_intersection()
    {
        // A non-scoped role (e.g. Finance) sees everything within its permissions — no scope narrowing,
        // even with zero claims.
        var user = ContextWith("Finance", [], [], []);
        Assert.True(user.CanAccessScope(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public void Presidency_bypasses_all_scope_like_admin()
    {
        // Presidency (AEL-883) sees everything: it bypasses the intersection scope AND the company scope,
        // like Admin, even with zero grants. (Its admin:users block is permission-based, tested elsewhere.)
        var user = ContextWith(Roles.Presidency, [], [], []);
        Assert.True(user.IsPresident);
        Assert.False(user.IsAdmin);
        Assert.True(user.CanAccessScope(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));
        Assert.True(user.CanAccess(Guid.NewGuid())); // AEL-764 company scope bypass
    }

    // ---- CanAccessScope: scoped role (Requester/Approver) ----------------------------------------

    [Theory]
    [InlineData("Requester")]
    [InlineData("Approver")]
    public void Scoped_role_with_all_three_dimensions_granted_is_allowed(string role)
    {
        var company = Guid.NewGuid();
        var unit = Guid.NewGuid();
        var costCenter = Guid.NewGuid();
        var user = ContextWith(role, [company], [(company, unit)], [(company, costCenter)]);

        Assert.True(user.CanAccessScope(company, unit, costCenter));
    }

    [Fact]
    public void Scoped_role_with_no_claims_is_blocked()
    {
        // Explicit lists: an empty dimension blocks (no "empty = all").
        var user = ContextWith("Requester", [], [], []);
        Assert.False(user.CanAccessScope(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public void Scoped_role_missing_the_unit_dimension_is_blocked()
    {
        var company = Guid.NewGuid();
        var costCenter = Guid.NewGuid();
        // Company + cost center granted, but NO unit granted ⇒ the empty unit dimension blocks.
        var user = ContextWith("Requester", [company], [], [(company, costCenter)]);

        Assert.False(user.CanAccessScope(company, Guid.NewGuid(), costCenter));
    }

    [Fact]
    public void Scoped_role_missing_the_cost_center_dimension_is_blocked()
    {
        var company = Guid.NewGuid();
        var unit = Guid.NewGuid();
        // Company + unit granted, but NO cost center granted ⇒ the empty cost-center dimension blocks.
        var user = ContextWith("Requester", [company], [(company, unit)], []);

        Assert.False(user.CanAccessScope(company, unit, Guid.NewGuid()));
    }

    [Fact]
    public void Scoped_role_with_an_unlisted_unit_is_blocked()
    {
        var company = Guid.NewGuid();
        var granted = Guid.NewGuid();
        var other = Guid.NewGuid();
        var costCenter = Guid.NewGuid();
        var user = ContextWith("Requester", [company], [(company, granted)], [(company, costCenter)]);

        Assert.True(user.CanAccessScope(company, granted, costCenter));
        Assert.False(user.CanAccessScope(company, other, costCenter)); // a unit not in the list
    }

    [Fact]
    public void Scoped_role_with_an_unlisted_cost_center_is_blocked()
    {
        var company = Guid.NewGuid();
        var unit = Guid.NewGuid();
        var granted = Guid.NewGuid();
        var other = Guid.NewGuid();
        var user = ContextWith("Requester", [company], [(company, unit)], [(company, granted)]);

        Assert.True(user.CanAccessScope(company, unit, granted));
        Assert.False(user.CanAccessScope(company, unit, other)); // a cost center not in the list
    }

    [Fact]
    public void Scoped_role_on_a_non_granted_company_is_blocked()
    {
        var granted = Guid.NewGuid();
        var notGranted = Guid.NewGuid();
        var unit = Guid.NewGuid();
        var costCenter = Guid.NewGuid();
        var user = ContextWith("Requester", [granted], [(granted, unit)], [(granted, costCenter)]);

        // Same unit/cost-center guids but a different company ⇒ blocked (the company dimension fails).
        Assert.False(user.CanAccessScope(notGranted, unit, costCenter));
    }

    [Fact]
    public void Scoped_role_scopes_do_not_leak_across_companies()
    {
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var unitA = Guid.NewGuid();
        var costCenterA = Guid.NewGuid();
        // The unit and cost center are granted under company A only.
        var user = ContextWith("Requester", [companyA, companyB], [(companyA, unitA)], [(companyA, costCenterA)]);

        Assert.True(user.CanAccessScope(companyA, unitA, costCenterA));
        // Company B is granted, but it has no unit/cost-center rows of its own ⇒ blocked (empty dimensions).
        Assert.False(user.CanAccessScope(companyB, unitA, costCenterA));
    }

    // ---- Single-dimension picker helpers (form pickers) ------------------------------------------

    [Fact]
    public void IsUnitInScope_and_IsCostCenterInScope_gate_the_scoped_role()
    {
        var company = Guid.NewGuid();
        var unit = Guid.NewGuid();
        var costCenter = Guid.NewGuid();
        var user = ContextWith("Requester", [company], [(company, unit)], [(company, costCenter)]);

        Assert.True(user.IsUnitInScope(company, unit));
        Assert.False(user.IsUnitInScope(company, Guid.NewGuid()));
        Assert.True(user.IsCostCenterInScope(company, costCenter));
        Assert.False(user.IsCostCenterInScope(company, Guid.NewGuid()));
    }

    [Fact]
    public void Single_dimension_pickers_bypass_for_a_non_scoped_role()
    {
        var user = ContextWith("Finance", [], [], []);
        Assert.True(user.IsUnitInScope(Guid.NewGuid(), Guid.NewGuid()));
        Assert.True(user.IsCostCenterInScope(Guid.NewGuid(), Guid.NewGuid()));
    }

    // ---- Live snapshot intersection (immediate revocation path) ----------------------------------

    [Fact]
    public void UserScopeSnapshot_covers_only_on_full_intersection()
    {
        var company = Guid.NewGuid();
        var unit = Guid.NewGuid();
        var costCenter = Guid.NewGuid();
        var snapshot = new UserScopeSnapshot([company], [unit], [costCenter]);

        Assert.True(snapshot.Covers(company, unit, costCenter));
        Assert.False(snapshot.Covers(Guid.NewGuid(), unit, costCenter));      // company misses
        Assert.False(snapshot.Covers(company, Guid.NewGuid(), costCenter));   // unit misses
        Assert.False(snapshot.Covers(company, unit, Guid.NewGuid()));         // cost center misses
    }

    [Fact]
    public void Empty_snapshot_covers_nothing()
    {
        Assert.False(UserScopeSnapshot.Empty.Covers(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));
    }

    // ---- Role literals pinned to the catalog (guard against a silent fail-open on rename) ---------

    [Fact]
    public void IsScopedRole_is_pinned_to_the_Requester_and_Approver_constants()
    {
        // UserContext hard-codes "Requester"/"Approver" literals (to avoid coupling Common → Identity.Domain).
        // Pin them to the Roles catalog so a rename fails HERE, not silently as a dropped scope check
        // (IsScopedRole=false ⇒ CanAccessScope bypasses ⇒ fail-OPEN).
        Assert.Equal("Requester", Roles.Requester);
        Assert.Equal("Approver", Roles.Approver);
        Assert.True(ContextWith(Roles.Requester, [], [], []).IsScopedRole);
        Assert.True(ContextWith(Roles.Approver, [], [], []).IsScopedRole);
        Assert.False(ContextWith(Roles.Finance, [], [], []).IsScopedRole);
        Assert.False(ContextWith(Roles.Admin, [], [], []).IsScopedRole);
    }

    [Fact]
    public void IsFinalApprover_is_approve_without_write_or_triage()
    {
        Assert.True(ContextWithPermissions(Roles.Approver, Permissions.RequisitionsApprove).IsFinalApprover);
        // approve + write (a hypothetical dual role) is NOT the final approver — it falls to the JWT branch.
        Assert.False(ContextWithPermissions(Roles.Approver, Permissions.RequisitionsApprove, Permissions.RequisitionsWrite).IsFinalApprover);
        // triage-only (Controllership) is not the final approver.
        Assert.False(ContextWithPermissions(Roles.Controllership, Permissions.RequisitionsTriage).IsFinalApprover);
        // Admin bypasses everything — never the "final approver".
        Assert.False(ContextWithPermissions(Roles.Admin, Permissions.RequisitionsApprove).IsFinalApprover);
    }

    // ---- CanAccess: company grants as an opt-in filter (AEL-906) ----------------------------------

    [Theory]
    [InlineData("Finance")]
    [InlineData("Controllership")]
    [InlineData("Accounting")]
    public void Management_role_with_no_grants_accesses_every_company(string role)
    {
        // These roles run the whole group and are never wired to specific companies (no scope editor for them),
        // so with zero grants they are unscoped and reach every company (AEL-906).
        var user = ContextWith(role, [], [], []);
        Assert.True(user.CanSeeConsolidated);
        Assert.True(user.CanAccess(Guid.NewGuid()));
        Assert.True(user.CanAccess(Guid.NewGuid()));
    }

    [Fact]
    public void Management_role_with_a_grant_is_restricted_to_it()
    {
        var granted = Guid.NewGuid();
        var other = Guid.NewGuid();
        // A grant flips a management user from "sees all" to the restricted set (grants are an opt-in filter).
        var user = ContextWith(Roles.Finance, [granted], [], []);
        Assert.True(user.CanAccess(granted));
        Assert.False(user.CanAccess(other));
    }

    [Theory]
    [InlineData("Requester")]
    [InlineData("Approver")]
    public void Scoped_role_can_access_only_its_granted_companies(string role)
    {
        var granted = Guid.NewGuid();
        var other = Guid.NewGuid();
        var user = ContextWith(role, [granted], [], []);
        Assert.True(user.CanAccess(granted));
        Assert.False(user.CanAccess(other));
    }

    [Theory]
    [InlineData("Requester")]
    [InlineData("Approver")]
    public void Scoped_role_with_no_grants_accesses_nothing(string role)
    {
        Assert.False(ContextWith(role, [], [], []).CanAccess(Guid.NewGuid()));
    }

    [Fact]
    public void Plain_User_role_with_no_grants_accesses_nothing()
    {
        // Regression guard (AEL-906): the unscoped clause keys on CanSeeConsolidated, NOT !IsScopedRole —
        // otherwise the default User role (neither scoped nor management) would fail OPEN with no grants.
        var user = ContextWith(Roles.User, [], [], []);
        Assert.False(user.CanSeeConsolidated);
        Assert.False(user.CanAccess(Guid.NewGuid()));
    }

    [Fact]
    public void CanSeeConsolidated_is_exactly_the_management_set()
    {
        Assert.True(ContextWith(Roles.Admin, [], [], []).CanSeeConsolidated);
        Assert.True(ContextWith(Roles.Presidency, [], [], []).CanSeeConsolidated);
        Assert.True(ContextWith(Roles.Controllership, [], [], []).CanSeeConsolidated);
        Assert.True(ContextWith(Roles.Finance, [], [], []).CanSeeConsolidated);
        Assert.True(ContextWith(Roles.Accounting, [], [], []).CanSeeConsolidated);
        Assert.False(ContextWith(Roles.Requester, [], [], []).CanSeeConsolidated);
        Assert.False(ContextWith(Roles.Approver, [], [], []).CanSeeConsolidated);
        Assert.False(ContextWith(Roles.User, [], [], []).CanSeeConsolidated);
    }
}
