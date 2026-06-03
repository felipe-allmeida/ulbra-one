using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application;
using Ulbra.One.Core.Infrastructure.Database;
using Ulbra.One.Financeiro.Domain;
using Ulbra.One.Financeiro.Domain.Entities;
using Ulbra.One.Financeiro.Infrastructure.Database;

namespace Ulbra.One.Financeiro.Application.Endpoints;

public sealed class LancamentosEndpoint : IEndpoint
{
    public record CreateLancamentoRequest(
        Guid EmpresaId, Guid UnidadeId, Guid CentroDeCustoId, TipoLancamento Tipo,
        string Descricao, string Contraparte, decimal Valor, DateOnly Vencimento);
    public record RejeitarRequest(string? Motivo);

    public void Map(IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/lancamentos").WithTags("Financeiro");

        group.MapGet("/", async (FinanceiroContext db, Guid? empresaId, TipoLancamento? tipo, StatusLancamento? status, Guid? centroDeCustoId, CancellationToken ct) =>
        {
            var q = db.Lancamentos.AsQueryable();
            if (empresaId is not null) q = q.Where(l => l.EmpresaId == empresaId);
            if (tipo is not null) q = q.Where(l => l.Tipo == tipo);
            if (status is not null) q = q.Where(l => l.Status == status);
            if (centroDeCustoId is not null) q = q.Where(l => l.CentroDeCustoId == centroDeCustoId);
            return Results.Ok(await q.OrderBy(l => l.Vencimento).ToListAsync(ct));
        }).WithName("ListarLancamentos");

        group.MapGet("/{id:guid}", async (Guid id, FinanceiroContext db, CancellationToken ct) =>
            await db.Lancamentos.FirstOrDefaultAsync(l => l.Id == id, ct) is { } l ? Results.Ok(l) : Results.NotFound())
            .WithName("ObterLancamento");

        group.MapPost("/", async (CreateLancamentoRequest req, FinanceiroContext db, CoreContext core, CancellationToken ct) =>
        {
            var cc = await core.CostCenters.FirstOrDefaultAsync(c => c.Id == req.CentroDeCustoId && c.CompanyId == req.EmpresaId, ct);
            if (cc is null) return Results.Problem("Centro de custo inválido para esta empresa.", statusCode: StatusCodes.Status400BadRequest);

            var unidadeOk = await core.Units.AnyAsync(u => u.Id == req.UnidadeId && u.CompanyId == req.EmpresaId, ct);
            if (!unidadeOk) return Results.Problem("Unidade inválida para esta empresa.", statusCode: StatusCodes.Status400BadRequest);

            if (req.Valor <= 0) return Results.Problem("Valor deve ser maior que zero.", statusCode: StatusCodes.Status400BadRequest);

            var l = new Lancamento(req.EmpresaId, req.UnidadeId, req.CentroDeCustoId, req.Tipo,
                req.Descricao, req.Contraparte, req.Valor, req.Vencimento);
            l.AplicarAutoAprovacao(cc.AutoApprovalLimit);

            db.Lancamentos.Add(l);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/lancamentos/{l.Id}", l);
        }).WithName("CriarLancamento");

        group.MapPost("/{id:guid}/aprovar", async (Guid id, FinanceiroContext db, CancellationToken ct) =>
            await Transicao(db, id, l => l.Aprovar(), ct)).WithName("AprovarLancamento");

        group.MapPost("/{id:guid}/rejeitar", async (Guid id, RejeitarRequest req, FinanceiroContext db, CancellationToken ct) =>
            await Transicao(db, id, l => l.Rejeitar(req.Motivo), ct)).WithName("RejeitarLancamento");

        group.MapPost("/{id:guid}/pagar", async (Guid id, FinanceiroContext db, CancellationToken ct) =>
        {
            var l = await db.Lancamentos.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (l is null) return Results.NotFound();
            try { l.MarcarPago(); }
            catch (InvalidOperationException ex) { return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict); }
            await db.SaveChangesAsync(ct);
            return Results.Ok(l);
        }).WithName("PagarLancamento");
    }

    private static async Task<IResult> Transicao(FinanceiroContext db, Guid id, Action<Lancamento> acao, CancellationToken ct)
    {
        var l = await db.Lancamentos.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (l is null) return Results.NotFound();
        acao(l);
        await db.SaveChangesAsync(ct);
        return Results.Ok(l);
    }
}
