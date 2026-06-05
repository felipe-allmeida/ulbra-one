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

namespace Ulbra.One.Core.Application.Endpoints.Units.CreateUnit;

public sealed class CreateUnit : IEndpoint
{
    public sealed record Request
    {
        public Guid CompanyId { get; set; }
        [NormalizeText] public string Name { get; set; } = string.Empty;
        [NormalizeText] public string Acronym { get; set; } = string.Empty;
    }

    public void Map(IEndpointRouteBuilder builder)
    {
        builder.MapPost("", Handle)
            .WithName("CreateUnit")
            .WithTags("Core")
            .RequireAuthorization()
            .WithRequestNormalizer<Request>()
            .Produces<UnitResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> Handle(
        Request req,
        [FromKeyedServices("core")] IUnitOfWork uow,
        CancellationToken ct)
    {
        if (!await uow.ReadSet<Company>().AnyAsync(e => e.Id == req.CompanyId, ct))
            return Results.Problem(detail: $"Company {req.CompanyId} not found.", statusCode: StatusCodes.Status404NotFound, title: "Company.NotFound");

        var unit = new Unit(req.CompanyId, req.Name, req.Acronym);
        await uow.AddAsync(unit, ct);
        await uow.SaveChangesAsync(ct);
        return Results.Created($"/api/units/{unit.Id}", UnitResponse.From(unit));
    }
}
