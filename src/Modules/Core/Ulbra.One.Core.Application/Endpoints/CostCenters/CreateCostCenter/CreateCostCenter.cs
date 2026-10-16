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

namespace Ulbra.One.Core.Application.Endpoints.CostCenters.CreateCostCenter;

public sealed class CreateCostCenter : IEndpoint
{
    public sealed record Request
    {
        [SwaggerSchema("Company the cost center belongs to.")]
        public CompanyId CompanyId { get; set; }

        [SwaggerSchema("Accounting code of the cost center. Unique within the company and what the list is "
            + "ordered by.")]
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

        [SwaggerSchema("Units allowed to charge this cost center. Each one becomes a link, and only a "
            + "linked unit can be given a monthly budget under it.")]
        public UnitId[] UnitIds { get; set; } = [];
    }

    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapPost("", Handle)
            .WithName("CreateCostCenter")
            .WithTags("Core")
            .WithSummary("Creates a cost center.")
            .WithDescription("Registers a cost center (natureza) under a company and links it to the units "
                + "that may charge it. It owns no budget yet — the monthly ceilings are created per unit "
                + "through the unit-budgets routes.")
            .RequirePermission(Permissions.CoreWrite)
            .WithRequestNormalizer<Request>()
            .WithValidation<Request>()
            .Produces<CostCenterResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> Handle(
        Request req,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        if (!await uow.ReadSet<Company>().AnyAsync(e => e.Id == req.CompanyId.Value, ct))
            return ErrorResults.NotFound($"Company {req.CompanyId} not found.", ErrorCodes.Company.NotFound);

        var unitIds = (req.UnitIds ?? []).Select(u => u.Value).Distinct().ToArray();
        if (!await UnitReconciliation.UnitsValid(uow, req.CompanyId.Value, unitIds, ct))
            return ErrorResults.InvalidRequest("One or more units do not belong to this company.", ErrorCodes.CostCenter.UnitsInvalid);

        var costCenter = new CostCenter(req.CompanyId.Value, req.Code, req.Name, req.AutoApprovalLimit);
        await uow.AddAsync(costCenter, ct);
        foreach (var uid in unitIds)
            await uow.AddAsync(new CostCenterUnit(costCenter.Id, uid), ct);

        await uow.SaveChangesAsync(ct);
        // A brand-new cost center has no budget cells yet — the roll-up is zero until they are added.
        return TypedResults.Created($"/api/cost-centers/{new CostCenterId(costCenter.Id)}", CostCenterResponse.From(costCenter, unitIds, 0m));
    }
}
