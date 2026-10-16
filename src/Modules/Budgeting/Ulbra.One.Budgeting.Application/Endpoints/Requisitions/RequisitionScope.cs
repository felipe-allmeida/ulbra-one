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

        if (userContext.IsScopedRole)
        {
            Guid[] scopedCompanyIds, scopedUnitIds, scopedCostCenterIds;
            if (userContext.IsFinalApprover)
            {
                var scope = await userScope.GetAsync(Guid.Parse(userContext.UserId), ct);
                scopedCompanyIds = scope.CompanyIds.ToArray();
                scopedUnitIds = scope.UnitIds.ToArray();
                scopedCostCenterIds = scope.CostCenterIds.ToArray();
            }
            else
            {
                scopedCompanyIds = userContext.Scopes.Select(s => s.CompanyId).ToArray();
                scopedUnitIds = userContext.UnitScopes.Select(s => s.UnitId).ToArray();
                scopedCostCenterIds = userContext.CostCenterScopes.Select(s => s.CostCenterId).ToArray();
            }
            query = query.Where(r => scopedCompanyIds.Contains(r.CompanyId)
                && scopedUnitIds.Contains(r.UnitId)
                && scopedCostCenterIds.Contains(r.CostCenterId));
        }

        return query;
    }
}
