using Microsoft.EntityFrameworkCore;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Application.Realtime;

namespace Ulbra.One.Budgeting.Infrastructure.Realtime;

/// <summary>Espelha a leitura de GetRequisition: existe, e está no escopo Empresa × Unidade × Centro de
/// Custo do chamador quando ele tem role escopada. Se divergir de GetRequisition, a presença passa a
/// revelar recurso que o usuário não pode abrir.</summary>
public sealed class RequisitionPresenceAuthorizer(
    IUnitOfWork uow,
    UserContext userContext,
    IUserScopeDirectory userScope) : IPresenceAuthorizer
{
    public string Scope => "requisition";

    public async Task<bool> CanViewAsync(Guid resourceId, CancellationToken ct)
    {
        if (!userContext.HasPermission(Permissions.RequisitionsRead)) return false;

        var requisition = await uow.ReadSet<Requisition>()
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == resourceId, ct);
        if (requisition is null) return false;
        if (!userContext.IsScopedRole) return true;

        return userContext.IsFinalApprover
            ? (await userScope.GetAsync(Guid.Parse(userContext.UserId), ct))
                .Covers(requisition.CompanyId, requisition.UnitId, requisition.CostCenterId)
            : userContext.CanAccessScope(requisition.CompanyId, requisition.UnitId, requisition.CostCenterId);
    }
}
