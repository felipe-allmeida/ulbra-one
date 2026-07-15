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

namespace Ulbra.One.Core.Application.Endpoints.Units.CreateUnit;

public sealed class CreateUnit : IEndpoint
{
    public sealed record Request
    {
        public CompanyId CompanyId { get; set; }
        [NormalizeText] public string Name { get; set; } = string.Empty;
        [NormalizeText] public string Acronym { get; set; } = string.Empty;
    }

    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapPost("", Handle)
            .WithName("CreateUnit")
            .WithTags("Core")
            .RequirePermission(Permissions.CoreWrite)
            .WithRequestNormalizer<Request>()
            .Produces<UnitResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> Handle(
        Request req,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        if (!await uow.ReadSet<Company>().AnyAsync(e => e.Id == req.CompanyId.Value, ct))
            return TypedResults.Problem(detail: $"Company {req.CompanyId} not found.", statusCode: StatusCodes.Status404NotFound, title: "Company.NotFound");

        var unit = new Unit(req.CompanyId.Value, req.Name, req.Acronym);
        await uow.AddAsync(unit, ct);
        await uow.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/units/{new UnitId(unit.Id)}", UnitResponse.From(unit));
    }
}
