using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application;
using Ulbra.One.Core.Application.Contracts;
using Ulbra.One.Core.Domain.Entities;
using Ulbra.One.Core.Infrastructure.Database;

namespace Ulbra.One.Core.Application.Endpoints;

public sealed class UnitsEndpoint : IEndpoint
{
    public record CreateUnitRequest(Guid CompanyId, string Name, string Acronym);
    public record UpdateUnitRequest(string Name, string Acronym);

    public void Map(IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/units").WithTags("Core").RequireAuthorization();

        group.MapGet("/", async (CoreContext db, Guid? companyId, CancellationToken ct) =>
        {
            var query = db.Units.AsQueryable();
            if (companyId is not null) query = query.Where(u => u.CompanyId == companyId);
            var units = await query.OrderBy(u => u.Name).ToListAsync(ct);
            return TypedResults.Ok(units.Select(UnitResponse.From).ToList());
        })
            .WithName("ListUnits")
            .Produces<List<UnitResponse>>(StatusCodes.Status200OK);

        group.MapPost("/", async (CreateUnitRequest req, CoreContext db, CancellationToken ct) =>
        {
            if (!await db.Companies.AnyAsync(e => e.Id == req.CompanyId, ct))
                return Results.Problem(detail: $"Company {req.CompanyId} not found.", statusCode: StatusCodes.Status404NotFound, title: "Company.NotFound");

            var unit = new Unit(req.CompanyId, req.Name, req.Acronym);
            db.Units.Add(unit);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/units/{unit.Id}", UnitResponse.From(unit));
        })
            .WithName("CreateUnit")
            .Produces<UnitResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}", async (Guid id, UpdateUnitRequest req, CoreContext db, CancellationToken ct) =>
        {
            var unit = await db.Units.FirstOrDefaultAsync(u => u.Id == id, ct);
            if (unit is null) return Results.NotFound();
            unit.Update(req.Name, req.Acronym);
            await db.SaveChangesAsync(ct);
            return Results.Ok(UnitResponse.From(unit));
        })
            .WithName("UpdateUnit")
            .Produces<UnitResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
    }
}
