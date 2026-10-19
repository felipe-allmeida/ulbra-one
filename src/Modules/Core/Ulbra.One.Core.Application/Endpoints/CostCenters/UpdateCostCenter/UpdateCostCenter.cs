using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application.Validation;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Application.Normalization;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Core.Application.Contracts;
using Ulbra.One.Core.Domain.Entities;

namespace Ulbra.One.Core.Application.Endpoints.CostCenters.UpdateCostCenter;

public sealed class UpdateCostCenter : IEndpoint
{
    public sealed record Request
    {
        [SwaggerSchema("Accounting code of the cost center. Unique within the company.")]
        [SwaggerExample("3.01.02")]
        [NormalizeText] public string Code { get; set; } = string.Empty;

        [SwaggerSchema("Cost center name (natureza).")]
        [SwaggerExample("Manutenção Predial")]
        [NormalizeText] public string Name { get; set; } = string.Empty;

        [SwaggerSchema("Ceiling for automatic approval, in BRL. A payable charged to this cost center whose "
            + "total is at or below it is approved WITHOUT human review when it gets classified — it does "
            + "not stop at the approval queue. Zero disables it. This does not apply to requisitions, "
            + "which are always triaged, complemented and approved.")]
        [SwaggerExample("0.00")]
        public decimal AutoApprovalLimit { get; set; }

        [SwaggerSchema("Units allowed to charge this cost center. This REPLACES the current set of links, "
            + "so omitting a unit unlinks it — send the full list, not just the additions.")]
        public UnitId[] UnitIds { get; set; } = [];
    }

    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapPut("/{id}", Handle)
            .WithName("UpdateCostCenter")
            .WithTags("Core")
            .WithSummary("Updates a cost center.")
            .WithDescription("Replaces the cost center's editable fields and its whole set of unit links. "
                + "A full replace, not a patch: a unit missing from UnitIds is unlinked. The owning company "
                + "is immutable, and existing monthly budgets are not touched here.")
            .RequirePermission(Permissions.CoreWrite)
            .WithRequestNormalizer<Request>()
            .WithValidation<Request>()
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
            return ErrorResults.InvalidRequest("One or more units do not belong to this company.", ErrorCodes.CostCenter.UnitsInvalid);

        costCenter.Update(req.Code, req.Name, req.AutoApprovalLimit);

        var current = await uow.Set<CostCenterUnit>().Where(x => x.CostCenterId == id.Value).ToListAsync(ct);
        uow.RemoveRange(current.Where(x => !unitIds.Contains(x.UnitId)));
        var existing = current.Select(x => x.UnitId).ToHashSet();
        foreach (var uid in unitIds.Where(u => !existing.Contains(u)))
            await uow.AddAsync(new CostCenterUnit(id.Value, uid), ct);

        await uow.SaveChangesAsync(ct);
        var annualBudget = await uow.ReadSet<CostCenterUnitBudget>()
            .Where(b => b.CostCenterId == id.Value && b.Active).SumAsync(b => (decimal?)b.MonthlyLimit, ct) ?? 0m;
        return TypedResults.Ok(CostCenterResponse.From(costCenter, unitIds, annualBudget));
    }
}
