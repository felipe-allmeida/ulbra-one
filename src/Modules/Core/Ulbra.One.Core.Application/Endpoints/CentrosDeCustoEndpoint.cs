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
        Guid EmpresaId, string Codigo, string Nome, decimal OrcamentoAnual, decimal LimiteAutoAprovacao, Guid[] UnidadeIds);
    public record UpdateCentroDeCustoRequest(
        string Codigo, string Nome, decimal OrcamentoAnual, decimal LimiteAutoAprovacao, Guid[] UnidadeIds);

    public void Map(IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/centros-custo").WithTags("Cadastros");

        group.MapGet("/", async (CoreContext db, Guid? empresaId, CancellationToken ct) =>
        {
            var query = db.CentrosDeCusto.AsQueryable();
            if (empresaId is not null) query = query.Where(c => c.EmpresaId == empresaId);

            var centros = await query.OrderBy(c => c.Codigo)
                .Select(c => new
                {
                    c.Id, c.EmpresaId, c.Codigo, c.Nome, c.OrcamentoAnual, c.LimiteAutoAprovacao, c.Ativo,
                    unidadeIds = db.CentroDeCustoUnidades.Where(x => x.CentroDeCustoId == c.Id).Select(x => x.UnidadeId).ToList(),
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
            if (!await UnidadesValidas(db, req.EmpresaId, unidadeIds, ct))
                return Results.Problem("Uma ou mais unidades não pertencem a esta empresa.", statusCode: StatusCodes.Status400BadRequest);

            var centro = new CentroDeCusto(req.EmpresaId, req.Codigo, req.Nome, req.OrcamentoAnual, req.LimiteAutoAprovacao);
            db.CentrosDeCusto.Add(centro);
            foreach (var uid in unidadeIds)
                db.CentroDeCustoUnidades.Add(new CentroDeCustoUnidade(centro.Id, uid));

            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/centros-custo/{centro.Id}", Projetar(centro, unidadeIds));
        })
        .WithName("CriarCentroDeCusto");

        group.MapPut("/{id:guid}", async (Guid id, UpdateCentroDeCustoRequest req, CoreContext db, CancellationToken ct) =>
        {
            var centro = await db.CentrosDeCusto.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (centro is null) return Results.NotFound();

            var unidadeIds = (req.UnidadeIds ?? []).Distinct().ToArray();
            if (!await UnidadesValidas(db, centro.EmpresaId, unidadeIds, ct))
                return Results.Problem("Uma ou mais unidades não pertencem a esta empresa.", statusCode: StatusCodes.Status400BadRequest);

            centro.Atualizar(req.Codigo, req.Nome, req.OrcamentoAnual, req.LimiteAutoAprovacao);

            var atuais = await db.CentroDeCustoUnidades.Where(x => x.CentroDeCustoId == id).ToListAsync(ct);
            db.CentroDeCustoUnidades.RemoveRange(atuais.Where(x => !unidadeIds.Contains(x.UnidadeId)));
            var existentes = atuais.Select(x => x.UnidadeId).ToHashSet();
            foreach (var uid in unidadeIds.Where(u => !existentes.Contains(u)))
                db.CentroDeCustoUnidades.Add(new CentroDeCustoUnidade(id, uid));

            await db.SaveChangesAsync(ct);
            return Results.Ok(Projetar(centro, unidadeIds));
        })
        .WithName("AtualizarCentroDeCusto");
    }

    private static async Task<bool> UnidadesValidas(CoreContext db, Guid empresaId, Guid[] unidadeIds, CancellationToken ct)
    {
        if (unidadeIds.Length == 0) return true;
        var validas = await db.Unidades
            .Where(u => u.EmpresaId == empresaId && unidadeIds.Contains(u.Id))
            .Select(u => u.Id).ToListAsync(ct);
        return validas.Count == unidadeIds.Length;
    }

    private static object Projetar(CentroDeCusto c, Guid[] unidadeIds) => new
    {
        c.Id, c.EmpresaId, c.Codigo, c.Nome, c.OrcamentoAnual, c.LimiteAutoAprovacao, c.Ativo, unidadeIds,
    };
}
