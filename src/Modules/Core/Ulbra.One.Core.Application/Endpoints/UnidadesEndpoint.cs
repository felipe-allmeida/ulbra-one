using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application;
using Ulbra.One.Core.Domain.Entities;
using Ulbra.One.Core.Infrastructure.Database;

namespace Ulbra.One.Core.Application.Endpoints;

public sealed class UnidadesEndpoint : IEndpoint
{
    public record CreateUnidadeRequest(Guid EmpresaId, string Nome, string Sigla);
    public record UpdateUnidadeRequest(string Nome, string Sigla);

    public void Map(IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/unidades").WithTags("Cadastros");

        group.MapGet("/", async (CoreContext db, Guid? empresaId, CancellationToken ct) =>
        {
            var query = db.Unidades.AsQueryable();
            if (empresaId is not null) query = query.Where(u => u.EmpresaId == empresaId);
            return Results.Ok(await query.OrderBy(u => u.Nome).ToListAsync(ct));
        })
        .WithName("ListarUnidades");

        group.MapPost("/", async (CreateUnidadeRequest req, CoreContext db, CancellationToken ct) =>
        {
            if (!await db.Empresas.AnyAsync(e => e.Id == req.EmpresaId, ct))
                return Results.Problem($"Empresa {req.EmpresaId} não encontrada.", statusCode: StatusCodes.Status404NotFound);

            var unidade = new Unidade(req.EmpresaId, req.Nome, req.Sigla);
            db.Unidades.Add(unidade);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/unidades/{unidade.Id}", unidade);
        })
        .WithName("CriarUnidade");

        group.MapPut("/{id:guid}", async (Guid id, UpdateUnidadeRequest req, CoreContext db, CancellationToken ct) =>
        {
            var unidade = await db.Unidades.FirstOrDefaultAsync(u => u.Id == id, ct);
            if (unidade is null) return Results.NotFound();
            unidade.Atualizar(req.Nome, req.Sigla);
            await db.SaveChangesAsync(ct);
            return Results.Ok(unidade);
        })
        .WithName("AtualizarUnidade");
    }
}
