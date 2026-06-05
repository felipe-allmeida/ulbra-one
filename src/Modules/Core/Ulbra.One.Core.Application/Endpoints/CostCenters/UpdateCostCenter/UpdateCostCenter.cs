using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Infrastructure.Normalization;
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
        public Guid[] UnitIds { get; set; } = [];
    }

    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapPut("/{id:guid}", Handle)
            .WithName("UpdateCostCenter")
            .WithTags("Core")
            .RequireAuthorization()
            .WithRequestNormalizer<Request>()
            .Produces<CostCenterResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status400BadRequest);
    }

    private static async Task<IResult> Handle(
        Guid id,
        Request req,
        [FromKeyedServices("core")] IUnitOfWork uow,
        CancellationToken ct)
    {
        var costCenter = await uow.Set<CostCenter>().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (costCenter is null) return Results.NotFound();

        var unitIds = (req.UnitIds ?? []).Distinct().ToArray();
        if (!await UnitReconciliation.UnitsValid(uow, costCenter.CompanyId, unitIds, ct))
            return Results.Problem(detail: "One or more units do not belong to this company.", statusCode: StatusCodes.Status400BadRequest, title: "CostCenter.UnitsInvalid");

        costCenter.Update(req.Code, req.Name, req.AnnualBudget, req.AutoApprovalLimit);

        var current = await uow.Set<CostCenterUnit>().Where(x => x.CostCenterId == id).ToListAsync(ct);
        uow.RemoveRange(current.Where(x => !unitIds.Contains(x.UnitId)));
        var existing = current.Select(x => x.UnitId).ToHashSet();
        foreach (var uid in unitIds.Where(u => !existing.Contains(u)))
            await uow.AddAsync(new CostCenterUnit(id, uid), ct);

        await uow.SaveChangesAsync(ct);
        return Results.Ok(CostCenterResponse.From(costCenter, unitIds));
    }
}
