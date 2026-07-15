using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Application.Normalization;
using Ulbra.One.Core.Application.Contracts;
using Ulbra.One.Core.Domain.Entities;

namespace Ulbra.One.Core.Application.Endpoints.CostCenters.UpdateCostCenter;

public sealed class UpdateCostCenter : IEndpoint
{
    public sealed record Request
    {
        [NormalizeText] public string Code { get; set; } = string.Empty;
        [NormalizeText] public string Name { get; set; } = string.Empty;
        public decimal AnnualBudget { get; set; }
        public decimal AutoApprovalLimit { get; set; }
        public UnitId[] UnitIds { get; set; } = [];
    }

    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapPut("/{id}", Handle)
            .WithName("UpdateCostCenter")
            .WithTags("Core")
            .RequirePermission(Permissions.CoreWrite)
            .WithRequestNormalizer<Request>()
            .Produces<CostCenterResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest);
    }

    private static async Task<IResult> Handle(
        CostCenterId id,
        Request req,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var costCenter = await uow.Set<CostCenter>().FirstOrDefaultAsync(c => c.Id == id.Value, ct);
        if (costCenter is null) return TypedResults.NotFound();

        var unitIds = (req.UnitIds ?? []).Select(u => u.Value).Distinct().ToArray();
        if (!await UnitReconciliation.UnitsValid(uow, costCenter.CompanyId, unitIds, ct))
            return TypedResults.Problem(detail: "One or more units do not belong to this company.", statusCode: StatusCodes.Status400BadRequest, title: "CostCenter.UnitsInvalid");

        costCenter.Update(req.Code, req.Name, req.AnnualBudget, req.AutoApprovalLimit);

        var current = await uow.Set<CostCenterUnit>().Where(x => x.CostCenterId == id.Value).ToListAsync(ct);
        uow.RemoveRange(current.Where(x => !unitIds.Contains(x.UnitId)));
        var existing = current.Select(x => x.UnitId).ToHashSet();
        foreach (var uid in unitIds.Where(u => !existing.Contains(u)))
            await uow.AddAsync(new CostCenterUnit(id.Value, uid), ct);

        await uow.SaveChangesAsync(ct);
        return TypedResults.Ok(CostCenterResponse.From(costCenter, unitIds));
    }
}
