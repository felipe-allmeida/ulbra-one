using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application;
using Ulbra.One.Core.Domain.Entities;
using Ulbra.One.Core.Infrastructure.Database;

namespace Ulbra.One.Core.Application.Endpoints;

public sealed class CentrosDeCustoEndpoint : IEndpoint
{
    public record CreateCentroDeCustoRequest(
        Guid UnidadeId,
        string Codigo,
        string Nome,
        decimal OrcamentoAnual,
        Guid? CentroDeCustoPaiId);

    public void Map(IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/centros-custo").WithTags("Organograma");

        group.MapGet("/", async (CoreContext db, Guid? unidadeId, CancellationToken ct) =>
        {
            var query = db.CentrosDeCusto.AsQueryable();
            if (unidadeId is not null)
                query = query.Where(c => c.UnidadeId == unidadeId);
            return Results.Ok(await query.OrderBy(c => c.Codigo).ToListAsync(ct));
        })
        .WithName("ListarCentrosDeCusto");

        group.MapPost("/", async (CreateCentroDeCustoRequest req, CoreContext db, CancellationToken ct) =>
        {
            if (!await db.Unidades.AnyAsync(u => u.Id == req.UnidadeId, ct))
                return Results.Problem($"Unidade {req.UnidadeId} não encontrada.", statusCode: StatusCodes.Status404NotFound);

            if (req.CentroDeCustoPaiId is not null &&
                !await db.CentrosDeCusto.AnyAsync(c => c.Id == req.CentroDeCustoPaiId, ct))
                return Results.Problem($"Centro de custo pai {req.CentroDeCustoPaiId} não encontrado.", statusCode: StatusCodes.Status404NotFound);

            var centro = new CentroDeCusto(req.UnidadeId, req.Codigo, req.Nome, req.OrcamentoAnual, req.CentroDeCustoPaiId);
            db.CentrosDeCusto.Add(centro);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/centros-custo/{centro.Id}", centro);
        })
        .WithName("CriarCentroDeCusto");
    }
}
