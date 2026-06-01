using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application;
using Ulbra.One.Core.Domain.Entities;
using Ulbra.One.Core.Infrastructure.Database;

namespace Ulbra.One.Core.Application.Endpoints;

public sealed class EmpresasEndpoint : IEndpoint
{
    public record EmpresaRequest(string RazaoSocial, string Cnpj);

    public void Map(IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/empresas").WithTags("Cadastros");

        group.MapGet("/", async (CoreContext db, CancellationToken ct) =>
            Results.Ok(await db.Empresas.OrderBy(e => e.RazaoSocial).ToListAsync(ct)))
            .WithName("ListarEmpresas");

        group.MapPost("/", async (EmpresaRequest req, CoreContext db, CancellationToken ct) =>
        {
            var empresa = new Empresa(req.RazaoSocial, req.Cnpj);
            db.Empresas.Add(empresa);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/empresas/{empresa.Id}", empresa);
        })
        .WithName("CriarEmpresa");

        group.MapPut("/{id:guid}", async (Guid id, EmpresaRequest req, CoreContext db, CancellationToken ct) =>
        {
            var empresa = await db.Empresas.FirstOrDefaultAsync(e => e.Id == id, ct);
            if (empresa is null) return Results.NotFound();
            empresa.Atualizar(req.RazaoSocial, req.Cnpj);
            await db.SaveChangesAsync(ct);
            return Results.Ok(empresa);
        })
        .WithName("AtualizarEmpresa");
    }
}
