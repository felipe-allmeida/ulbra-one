using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application;
using Ulbra.One.Core.Domain.Entities;
using Ulbra.One.Core.Infrastructure.Database;

namespace Ulbra.One.Core.Application.Endpoints;

public sealed class CostCentersEndpoint : IEndpoint
{
    public record CreateCostCenterRequest(
        Guid CompanyId, string Code, string Name, decimal AnnualBudget, decimal AutoApprovalLimit, Guid[] UnitIds);
    public record UpdateCostCenterRequest(
        string Code, string Name, decimal AnnualBudget, decimal AutoApprovalLimit, Guid[] UnitIds);

    public void Map(IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/cost-centers").WithTags("Core");

        group.MapGet("/", async (CoreContext db, Guid? companyId, CancellationToken ct) =>
        {
            var query = db.CostCenters.AsQueryable();
            if (companyId is not null) query = query.Where(c => c.CompanyId == companyId);

            var costCenters = await query.OrderBy(c => c.Code)
                .Select(c => new
                {
                    c.Id, c.CompanyId, c.Code, c.Name, c.AnnualBudget, c.AutoApprovalLimit, c.Active,
                    unitIds = db.CostCenterUnits.Where(x => x.CostCenterId == c.Id).Select(x => x.UnitId).ToList(),
                })
                .ToListAsync(ct);

            return Results.Ok(costCenters);
        }).WithName("ListCostCenters");

        group.MapGet("/{id:guid}", async (Guid id, CoreContext db, CancellationToken ct) =>
        {
            var costCenter = await db.CostCenters.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (costCenter is null) return Results.NotFound();
            var unitIds = await db.CostCenterUnits
                .Where(x => x.CostCenterId == id).Select(x => x.UnitId).ToArrayAsync(ct);
            return Results.Ok(Project(costCenter, unitIds));
        }).WithName("GetCostCenter");

        group.MapPost("/", async (CreateCostCenterRequest req, CoreContext db, CancellationToken ct) =>
        {
            if (!await db.Companies.AnyAsync(e => e.Id == req.CompanyId, ct))
                return Results.Problem($"Empresa {req.CompanyId} não encontrada.", statusCode: StatusCodes.Status404NotFound);

            var unitIds = (req.UnitIds ?? []).Distinct().ToArray();
            if (!await UnitsValid(db, req.CompanyId, unitIds, ct))
                return Results.Problem("Uma ou mais unidades não pertencem a esta empresa.", statusCode: StatusCodes.Status400BadRequest);

            var costCenter = new CostCenter(req.CompanyId, req.Code, req.Name, req.AnnualBudget, req.AutoApprovalLimit);
            db.CostCenters.Add(costCenter);
            foreach (var uid in unitIds)
                db.CostCenterUnits.Add(new CostCenterUnit(costCenter.Id, uid));

            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/cost-centers/{costCenter.Id}", Project(costCenter, unitIds));
        }).WithName("CreateCostCenter");

        group.MapPut("/{id:guid}", async (Guid id, UpdateCostCenterRequest req, CoreContext db, CancellationToken ct) =>
        {
            var costCenter = await db.CostCenters.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (costCenter is null) return Results.NotFound();

            var unitIds = (req.UnitIds ?? []).Distinct().ToArray();
            if (!await UnitsValid(db, costCenter.CompanyId, unitIds, ct))
                return Results.Problem("Uma ou mais unidades não pertencem a esta empresa.", statusCode: StatusCodes.Status400BadRequest);

            costCenter.Update(req.Code, req.Name, req.AnnualBudget, req.AutoApprovalLimit);

            var current = await db.CostCenterUnits.Where(x => x.CostCenterId == id).ToListAsync(ct);
            db.CostCenterUnits.RemoveRange(current.Where(x => !unitIds.Contains(x.UnitId)));
            var existing = current.Select(x => x.UnitId).ToHashSet();
            foreach (var uid in unitIds.Where(u => !existing.Contains(u)))
                db.CostCenterUnits.Add(new CostCenterUnit(id, uid));

            await db.SaveChangesAsync(ct);
            return Results.Ok(Project(costCenter, unitIds));
        }).WithName("UpdateCostCenter");
    }

    private static async Task<bool> UnitsValid(CoreContext db, Guid companyId, Guid[] unitIds, CancellationToken ct)
    {
        if (unitIds.Length == 0) return true;
        var valid = await db.Units
            .Where(u => u.CompanyId == companyId && unitIds.Contains(u.Id))
            .Select(u => u.Id).ToListAsync(ct);
        return valid.Count == unitIds.Length;
    }

    private static object Project(CostCenter c, Guid[] unitIds) => new
    {
        c.Id, c.CompanyId, c.Code, c.Name, c.AnnualBudget, c.AutoApprovalLimit, c.Active, unitIds,
    };
}
