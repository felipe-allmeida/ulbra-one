using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Budgeting.Application.Contracts;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Budgeting.Application.Endpoints.Requisitions.GetRequisition;

public sealed class GetRequisition : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapGet("/{id}", Handle)
            .WithName("GetRequisition")
            .WithTags("Budgeting")
            .WithSummary("Gets a requisition.")
            .WithDescription("Returns a single requisition by id with its transition history, attachments "
                + "and installment plan. For its budget impact use "
                + "GET /api/requisitions/{id}/budget-control instead.")
            .RequirePermission(Permissions.RequisitionsRead)
            .Produces<RequisitionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> Handle(
        RequisitionId id,
        UserContext userContext,
        IUserScopeDirectory userScope,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var requisition = await uow.ReadSet<Requisition>()
            .Include(r => r.Attachments)
            .Include(r => r.Transitions)
            .Include(r => r.Installments) // authored payment plan shown in the detail panel (AEL-859)
            .Include(r => r.Cycles) // recurrence cycles + their generated Payables (AEL-877)
            .Include(r => r.Payments) // needed so RealizedValue reflects the real payments (AEL-793)
            .FirstOrDefaultAsync(r => r.Id == id.Value, ct);
        if (requisition is null) return TypedResults.NotFound();

        // Read scope (AEL-883): a scoped role reads only within its Empresa × Unidade × Centro de Custo
        // scope; 404-hide (same as the write gates). Approver reads LIVE; Requester from the JWT.
        if (userContext.IsScopedRole)
        {
            var inScope = userContext.IsFinalApprover
                ? (await userScope.GetAsync(Guid.Parse(userContext.UserId), ct))
                    .Covers(requisition.CompanyId, requisition.UnitId, requisition.CostCenterId)
                : userContext.CanAccessScope(requisition.CompanyId, requisition.UnitId, requisition.CostCenterId);
            if (!inScope) return TypedResults.NotFound();
        }

        return TypedResults.Ok(RequisitionResponse.From(requisition));
    }
}
