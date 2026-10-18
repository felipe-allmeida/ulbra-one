using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Ulbra.One.Budgeting.Application.Endpoints.Requisitions;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Identity.Domain;
using Xunit;

namespace Ulbra.One.UnitTests.Authentication;

/// <summary>Resolution of a caller's Empresa × Unidade × Centro de Custo scope sets (AEL-883 reuse): the same
/// branch <see cref="RequisitionScope.ApplyAsync"/> uses — non-scoped/Admin bypass (null = unrestricted),
/// Requester from JWT claims, final Approver live from <see cref="IUserScopeDirectory"/> — plus the
/// null-vs-empty distinction that keeps a scoped role fail-closed.</summary>
public class RequisitionScopeResolveTests
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
        return Build(claims);
    }

    private static UserContext ApproverContext(Guid userId, params string[] permissions)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Role, Roles.Approver),
            new(ClaimTypes.NameIdentifier, userId.ToString()),
        };
        foreach (var p in permissions) claims.Add(new Claim(Permissions.ClaimType, p));
        return Build(claims);
    }

    private static UserContext Build(List<Claim> claims)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } };
        return new UserContext(accessor);
    }

    private sealed class StubScopeDirectory(UserScopeSnapshot snapshot) : IUserScopeDirectory
    {
        public bool WasCalled { get; private set; }

        public Task<UserScopeSnapshot> GetAsync(Guid userId, CancellationToken ct)
        {
            WasCalled = true;
            return Task.FromResult(snapshot);
        }
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Controllership)]
    public async Task Non_scoped_role_is_unrestricted_and_never_reads_directory(string role)
    {
        var directory = new StubScopeDirectory(UserScopeSnapshot.Empty);

        var scope = await RequisitionScope.ResolveAsync(ContextWith(role, [], [], []), directory, CancellationToken.None);

        Assert.False(scope.Restricted);
        Assert.Null(scope.CompanyIds);
        Assert.Null(scope.UnitIds);
        Assert.Null(scope.CostCenterIds);
        Assert.False(directory.WasCalled);
    }

    [Fact]
    public async Task Requester_resolves_from_jwt_claims_and_never_reads_directory()
    {
        var company = Guid.NewGuid();
        var unit = Guid.NewGuid();
        var costCenter = Guid.NewGuid();
        var directory = new StubScopeDirectory(UserScopeSnapshot.Empty);
        var user = ContextWith(Roles.Requester, [company], [(company, unit)], [(company, costCenter)]);

        var scope = await RequisitionScope.ResolveAsync(user, directory, CancellationToken.None);

        Assert.True(scope.Restricted);
        Assert.Equal([company], scope.CompanyIds!);
        Assert.Equal([unit], scope.UnitIds!);
        Assert.Equal([costCenter], scope.CostCenterIds!);
        Assert.False(directory.WasCalled);
    }

    [Fact]
    public async Task Final_approver_resolves_live_from_directory()
    {
        var userId = Guid.NewGuid();
        var company = Guid.NewGuid();
        var unit = Guid.NewGuid();
        var costCenter = Guid.NewGuid();
        var directory = new StubScopeDirectory(new UserScopeSnapshot([company], [unit], [costCenter]));
        var user = ApproverContext(userId, Permissions.RequisitionsApprove);

        var scope = await RequisitionScope.ResolveAsync(user, directory, CancellationToken.None);

        Assert.True(directory.WasCalled);
        Assert.True(scope.Restricted);
        Assert.Equal([company], scope.CompanyIds!);
        Assert.Equal([unit], scope.UnitIds!);
        Assert.Equal([costCenter], scope.CostCenterIds!);
    }

    [Fact]
    public async Task Scoped_role_with_no_grants_is_empty_not_null_fail_closed()
    {
        var directory = new StubScopeDirectory(UserScopeSnapshot.Empty);

        var scope = await RequisitionScope.ResolveAsync(ContextWith(Roles.Requester, [], [], []), directory, CancellationToken.None);

        Assert.True(scope.Restricted);
        Assert.NotNull(scope.UnitIds);
        Assert.Empty(scope.UnitIds!);
        Assert.Empty(scope.CompanyIds!);
        Assert.Empty(scope.CostCenterIds!);
    }
}
