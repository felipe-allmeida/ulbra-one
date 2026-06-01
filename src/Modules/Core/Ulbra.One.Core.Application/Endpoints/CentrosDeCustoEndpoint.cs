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
        Guid EmpresaId,
        string Codigo,
        string Nome,
        decimal OrcamentoAnual,
        Guid[] UnidadeIds);

    public void Map(IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/centros-custo").WithTags("Cadastros");

        group.MapGet("/", async (CoreContext db, Guid? empresaId, CancellationToken ct) =>
        {
            var query = db.CentrosDeCusto.AsQueryable();
            if (empresaId is not null)
                query = query.Where(c => c.EmpresaId == empresaId);

            var centros = await query.OrderBy(c => c.Codigo)
                .Select(c => new
                {
                    c.Id,
                    c.EmpresaId,
                    c.Codigo,
                    c.Nome,
                    c.OrcamentoAnual,
                    c.Ativo,
                    unidadeIds = db.CentroDeCustoUnidades
                        .Where(x => x.CentroDeCustoId == c.Id)
                        .Select(x => x.UnidadeId)
                        .ToList(),
                })
                .ToListAsync(ct);

            return Results.Ok(centros);
        })
        .WithName("ListarCentrosDeCusto");

        group.MapPost("/", async (CreateCentroDeCustoRequest req, CoreContext db, CancellationToken ct) =>
        {
            if (!await db.Empresas.AnyAsync(e => e.Id == req.EmpresaId, ct))
                return Results.Problem($"Empresa {req.EmpresaId} não encontrada.", statusCode: StatusCodes.Status404NotFound);

            var unidadeIds = (req.UnidadeIds ?? []).Distinct().ToArray();
            if (unidadeIds.Length > 0)
            {
                var validas = await db.Unidades
                    .Where(u => u.EmpresaId == req.EmpresaId && unidadeIds.Contains(u.Id))
                    .Select(u => u.Id)
                    .ToListAsync(ct);

                if (validas.Count != unidadeIds.Length)
                    return Results.Problem("Uma ou mais unidades não pertencem a esta empresa.", statusCode: StatusCodes.Status400BadRequest);
            }

            var centro = new CentroDeCusto(req.EmpresaId, req.Codigo, req.Nome, req.OrcamentoAnual);
            db.CentrosDeCusto.Add(centro);
            foreach (var uid in unidadeIds)
                db.CentroDeCustoUnidades.Add(new CentroDeCustoUnidade(centro.Id, uid));

            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/centros-custo/{centro.Id}", new
            {
                centro.Id, centro.EmpresaId, centro.Codigo, centro.Nome, centro.OrcamentoAnual, centro.Ativo,
                unidadeIds,
            });
        })
        .WithName("CriarCentroDeCusto");
    }
}
