namespace Ulbra.One.Common.Application.Authentication;

/// <summary>A scoped user's data scope read live: the explicit companies, units and cost centers granted
/// (AEL-883). Used where revocation must be immediate (the Approver's approve/inbox), unlike the JWT claims
/// which only refresh on next login. Explicit lists — no "empty = all".</summary>
public sealed record UserScopeSnapshot(
    IReadOnlyCollection<Guid> CompanyIds,
    IReadOnlyCollection<Guid> UnitIds,
    IReadOnlyCollection<Guid> CostCenterIds)
{
    public static readonly UserScopeSnapshot Empty = new([], [], []);

    /// <summary>Intersection match: in scope only when company AND unit AND cost center are all granted.</summary>
    public bool Covers(Guid companyId, Guid unitId, Guid costCenterId) =>
        CompanyIds.Contains(companyId) && UnitIds.Contains(unitId) && CostCenterIds.Contains(costCenterId);
}

/// <summary>Resolves a user's scope (companies/units/cost centers) live from the store, without callers
/// referencing the Identity domain. Mirrors <see cref="IApprovalAuthority"/>. Fail-closed: unknown/unbound
/// user resolves to <see cref="UserScopeSnapshot.Empty"/> (matches nothing).</summary>
public interface IUserScopeDirectory
{
    Task<UserScopeSnapshot> GetAsync(Guid userId, CancellationToken ct);
}
