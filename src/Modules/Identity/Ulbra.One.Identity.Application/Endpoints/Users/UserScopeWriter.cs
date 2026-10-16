using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Identity.Application.Contracts;
using Ulbra.One.Identity.Domain.Entities;

namespace Ulbra.One.Identity.Application.Endpoints.Users;

/// <summary>The requested scope after validation: the granted companies plus the units / cost centers keyed
/// by their own id (each mapping to the company it was scoped under).</summary>
internal sealed record NormalizedScope(
    HashSet<Guid> Companies,
    Dictionary<Guid, Guid> UnitsByCompany,
    Dictionary<Guid, Guid> CostCentersByCompany)
{
    public static NormalizedScope Empty => new([], [], []);
}

/// <summary>Validation + persistence of a user's unified scope (Empresa × Unidade × Centro de custo), shared
/// by the endpoints that write it: <c>SetUserScope</c> (replace by diff) and <c>CreateUser</c> (pre-registration
/// with a scope already attached). Explicit lists — no "empty = all" (AEL-883).</summary>
internal static class UserScopeWriter
{
    /// <summary>Validates the requested scope. Returns <c>null</c> when valid (and fills
    /// <paramref name="scope"/>), or the 400 Problem to return otherwise.</summary>
    public static IResult? Validate(SetUserScopeRequest req, out NormalizedScope scope)
    {
        scope = NormalizedScope.Empty;

        var companies = (req.CompanyIds ?? []).Select(c => c.Value).Distinct().ToHashSet();
        if (companies.Contains(Guid.Empty))
            return Invalid("CompanyId cannot be empty.", ErrorCodes.User.ScopeInvalid);

        // Units/CCs keyed by their own id (unique per user), so a given id must map to a single company —
        // sending the same unit/cost center under two companies is a 400, not a 500 (a raw ToDictionary would
        // throw on the duplicate key).
        var units = new Dictionary<Guid, Guid>();
        foreach (var u in (req.Units ?? []).Select(u => (Company: u.CompanyId.Value, Unit: u.UnitId.Value)).Where(u => u.Unit != Guid.Empty))
        {
            if (units.TryGetValue(u.Unit, out var existing) && existing != u.Company)
                return Invalid("A unit cannot be scoped under more than one company.", ErrorCodes.User.ScopeInvalid);
            units[u.Unit] = u.Company;
        }

        var costCenters = new Dictionary<Guid, Guid>();
        foreach (var c in (req.CostCenters ?? []).Select(c => (Company: c.CompanyId.Value, CostCenter: c.CostCenterId.Value)).Where(c => c.CostCenter != Guid.Empty))
        {
            if (costCenters.TryGetValue(c.CostCenter, out var existing) && existing != c.Company)
                return Invalid("A cost center cannot be scoped under more than one company.", ErrorCodes.User.ScopeInvalid);
            costCenters[c.CostCenter] = c.Company;
        }

        if (units.Values.Any(companyId => !companies.Contains(companyId))
            || costCenters.Values.Any(companyId => !companies.Contains(companyId)))
            return Invalid("Every scoped unit / cost center must belong to a granted company.", ErrorCodes.User.ScopeCompanyMismatch);

        scope = new NormalizedScope(companies, units, costCenters);
        return null;
    }

    /// <summary>Writes the scope by diff against what the user has: grants what is new, revokes what was left
    /// out. Does not save — the caller commits.</summary>
    public static async Task ApplyAsync(IUnitOfWork uow, Guid userId, NormalizedScope scope, CancellationToken ct)
    {
        // Companies diff.
        var existingCompanies = await uow.Set<UserScopeGrant>().Where(g => g.UserId == userId).ToListAsync(ct);
        foreach (var companyId in scope.Companies.Except(existingCompanies.Select(g => g.CompanyId)))
            await uow.AddAsync(new UserScopeGrant(userId, companyId), ct);
        uow.RemoveRange(existingCompanies.Where(g => !scope.Companies.Contains(g.CompanyId)));

        // Units diff (keyed by unit). Revoking a company drops its units (they can't be in the desired set —
        // Validate rejects a unit whose company is not granted).
        var existingUnits = await uow.Set<UserUnitScopeGrant>().Where(g => g.UserId == userId).ToListAsync(ct);
        var currentUnits = existingUnits.Select(g => g.UnitId).ToHashSet();
        foreach (var (unitId, companyId) in scope.UnitsByCompany.Where(u => !currentUnits.Contains(u.Key)))
            await uow.AddAsync(new UserUnitScopeGrant(userId, companyId, unitId), ct);
        uow.RemoveRange(existingUnits.Where(g => !scope.UnitsByCompany.ContainsKey(g.UnitId)));

        // Cost centers diff (keyed by cost center).
        var existingCostCenters = await uow.Set<UserCostCenterScopeGrant>().Where(g => g.UserId == userId).ToListAsync(ct);
        var currentCostCenters = existingCostCenters.Select(g => g.CostCenterId).ToHashSet();
        foreach (var (costCenterId, companyId) in scope.CostCentersByCompany.Where(c => !currentCostCenters.Contains(c.Key)))
            await uow.AddAsync(new UserCostCenterScopeGrant(userId, companyId, costCenterId), ct);
        uow.RemoveRange(existingCostCenters.Where(g => !scope.CostCentersByCompany.ContainsKey(g.CostCenterId)));
    }

    private static IResult Invalid(string detail, string errorCode) =>
        ErrorResults.InvalidRequest(detail, errorCode);
}
