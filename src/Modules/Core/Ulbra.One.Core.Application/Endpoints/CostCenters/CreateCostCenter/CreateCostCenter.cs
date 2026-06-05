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

namespace Ulbra.One.Core.Application.Endpoints.CostCenters.CreateCostCenter;

public sealed class CreateCostCenter : IEndpoint
{
    public sealed record Request
    {
        public Guid CompanyId { get; set; }
        [NormalizeText] public string Code { get; set; } = string.Empty;
        [NormalizeText] public string Name { get; set; } = string.Empty;
        public decimal AnnualBudget { get; set; }
        public decimal AutoApprovalLimit { get; set; }
        public Guid[] UnitIds { get; set; } = [];
    }

    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapPost("", Handle)
            .WithName("CreateCostCenter")
            .WithTags("Core")
            .RequireAuthorization()
            .WithRequestNormalizer<Request>()
            .Produces<CostCenterResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> Handle(
        Request req,
        [FromKeyedServices("core")] IUnitOfWork uow,
        CancellationToken ct)
    {
        if (!await uow.ReadSet<Company>().AnyAsync(e => e.Id == req.CompanyId, ct))
            return Results.Problem(detail: $"Company {req.CompanyId} not found.", statusCode: StatusCodes.Status404NotFound, title: "Company.NotFound");

        var unitIds = (req.UnitIds ?? []).Distinct().ToArray();
        if (!await UnitReconciliation.UnitsValid(uow, req.CompanyId, unitIds, ct))
            return Results.Problem(detail: "One or more units do not belong to this company.", statusCode: StatusCodes.Status400BadRequest, title: "CostCenter.UnitsInvalid");

        var costCenter = new CostCenter(req.CompanyId, req.Code, req.Name, req.AnnualBudget, req.AutoApprovalLimit);
        await uow.AddAsync(costCenter, ct);
        foreach (var uid in unitIds)
            await uow.AddAsync(new CostCenterUnit(costCenter.Id, uid), ct);

        await uow.SaveChangesAsync(ct);
        return Results.Created($"/api/cost-centers/{costCenter.Id}", CostCenterResponse.From(costCenter, unitIds));
    }
}
