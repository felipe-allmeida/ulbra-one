using Microsoft.AspNetCore.Http;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Domain.Errors;

namespace Ulbra.One.Budgeting.Application.Endpoints.Requisitions;

/// <summary>
/// Company / ownership / role-scope filtering shared by every requisition read (browse list, stage counts).
/// Kept in one place so the counts can never drift from the rows the list returns for the same caller.
/// </summary>
public static class RequisitionScope
{
    /// <summary>
    /// Enforces the consolidated ("all companies") rule: omitting CompanyId is only allowed for a consolidated
    /// role, or a scoped role (whose scope filter already restricts it). Returns a 403 result when forbidden,
    /// otherwise null (caller proceeds).
    /// </summary>
    public static IResult? GuardConsolidated(CompanyId? companyId, UserContext userContext) =>
        companyId is null && !userContext.CanSeeConsolidated && !userContext.IsScopedRole
            ? ErrorResults.Forbidden("No access to the consolidated view.", ErrorCodes.Requisition.ConsolidatedForbidden)
            : null;

    /// <summary>
    /// Applies the company filter, the "mine" ownership filter and the Empresa × Unidade × Centro de Custo scope
    /// of a scoped role (Requester/Approver). The final Approver's scope is read LIVE (revocation is immediate);
    /// the Requester's travels in the JWT. Fail-closed: an empty scope yields no rows.
    /// </summary>
    public static async Task<IQueryable<Requisition>> ApplyAsync(
        IQueryable<Requisition> query,
        CompanyId? companyId,
        bool mine,
        UserContext userContext,
        IUserScopeDirectory userScope,
        CancellationToken ct)
    {
        if (companyId is { } cid) query = query.Where(r => r.CompanyId == cid.Value);

        // "mine": the owner is resolved server-side from the auth context — never a client-supplied id — and
        // matches the CreatedBy stamped by the audit interceptor (same NameIdentifier string), so it is exact.
        if (mine)
        {
            var ownerId = userContext.UserId;
            query = query.Where(r => r.CreatedBy == ownerId);
        }

        var scope = await ResolveAsync(userContext, userScope, ct);
        if (scope.Restricted)
        {
            Guid[] companyIds = scope.CompanyIds!, unitIds = scope.UnitIds!, costCenterIds = scope.CostCenterIds!;
            query = query.Where(r => companyIds.Contains(r.CompanyId)
                && unitIds.Contains(r.UnitId)
                && costCenterIds.Contains(r.CostCenterId));
        }

        return query;
    }

    /// <summary>
    /// Resolves the caller's Empresa × Unidade × Centro de Custo scope sets, so the requisition list and the
    /// budget reads recorte the same way. <c>null</c> lists = unrestricted (Admin and every non-scoped role
    /// bypass); non-null = an explicit allow-list (fail-closed — an empty set matches nothing). Branches like
    /// <see cref="ApplyAsync"/>: the final Approver is read LIVE via <see cref="IUserScopeDirectory"/>
    /// (revocation is immediate), the Requester from its JWT claims.
    /// </summary>
    public static async Task<RequisitionScopeSet> ResolveAsync(
        UserContext userContext, IUserScopeDirectory userScope, CancellationToken ct)
    {
        if (!userContext.IsScopedRole) return default;
        if (userContext.IsFinalApprover)
        {
            var scope = await userScope.GetAsync(Guid.Parse(userContext.UserId), ct);
            return new(scope.CompanyIds.ToArray(), scope.UnitIds.ToArray(), scope.CostCenterIds.ToArray());
        }
        return new(
            userContext.Scopes.Select(s => s.CompanyId).Distinct().ToArray(),
            userContext.UnitScopes.Select(s => s.UnitId).Distinct().ToArray(),
            userContext.CostCenterScopes.Select(s => s.CostCenterId).Distinct().ToArray());
    }

    /// <summary>Effective unit allow-list for a budget read: the unit picked in the form (if any) intersected
    /// with the caller's scope. Unrestricted caller → the picked unit alone, or <c>null</c> (whole company);
    /// scoped caller → the picked unit if it is in scope (else empty, fail-closed), or the whole scoped set.</summary>
    public static Guid[]? EffectiveUnitIds(RequisitionScopeSet scope, Guid? requestedUnitId)
    {
        if (!scope.Restricted)
            return requestedUnitId is { } u ? [u] : null;
        return requestedUnitId is { } r ? scope.UnitIds!.Where(x => x == r).ToArray() : scope.UnitIds!;
    }
}

/// <summary>
/// The Empresa × Unidade × Centro de Custo id sets a caller is scoped to (AEL-883). <c>null</c> in every
/// dimension means unrestricted (Admin / non-scoped roles); a non-null set is an explicit allow-list applied
/// fail-closed (an empty set matches nothing). See <see cref="RequisitionScope.ResolveAsync"/>.
/// </summary>
public readonly record struct RequisitionScopeSet(Guid[]? CompanyIds, Guid[]? UnitIds, Guid[]? CostCenterIds)
{
    /// <summary>True when the caller is a scoped role and the <c>Contains</c> filters must be applied.</summary>
    public bool Restricted => UnitIds is not null;
}
