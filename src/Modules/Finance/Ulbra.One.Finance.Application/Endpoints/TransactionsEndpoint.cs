using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application;
using Ulbra.One.Core.Infrastructure.Database;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Finance.Infrastructure.Database;

namespace Ulbra.One.Finance.Application.Endpoints;

public sealed class TransactionsEndpoint : IEndpoint
{
    public record CreateTransactionRequest(
        Guid CompanyId, Guid UnitId, Guid CostCenterId, TransactionType Type,
        string Description, string Counterparty, decimal Amount, DateOnly DueDate);
    public record RejectRequest(string? Reason);

    public void Map(IEndpointRouteBuilder builder)
    {
        var group = builder.MapGroup("/transactions").WithTags("Finance").RequireAuthorization();

        group.MapGet("/", async (FinanceContext db, Guid? companyId, TransactionType? type, TransactionStatus? status, Guid? costCenterId, CancellationToken ct) =>
        {
            var q = db.Transactions.AsQueryable();
            if (companyId is not null) q = q.Where(l => l.CompanyId == companyId);
            if (type is not null) q = q.Where(l => l.Type == type);
            if (status is not null) q = q.Where(l => l.Status == status);
            if (costCenterId is not null) q = q.Where(l => l.CostCenterId == costCenterId);
            return Results.Ok(await q.OrderBy(l => l.DueDate).ToListAsync(ct));
        }).WithName("ListTransactions");

        group.MapGet("/{id:guid}", async (Guid id, FinanceContext db, CancellationToken ct) =>
            await db.Transactions.FirstOrDefaultAsync(l => l.Id == id, ct) is { } l ? Results.Ok(l) : Results.NotFound())
            .WithName("GetTransaction");

        group.MapPost("/", async (CreateTransactionRequest req, FinanceContext db, CoreContext core, CancellationToken ct) =>
        {
            var cc = await core.CostCenters.FirstOrDefaultAsync(c => c.Id == req.CostCenterId && c.CompanyId == req.CompanyId, ct);
            if (cc is null) return Results.Problem("Centro de custo inválido para esta empresa.", statusCode: StatusCodes.Status400BadRequest);

            var unitOk = await core.Units.AnyAsync(u => u.Id == req.UnitId && u.CompanyId == req.CompanyId, ct);
            if (!unitOk) return Results.Problem("Unidade inválida para esta empresa.", statusCode: StatusCodes.Status400BadRequest);

            if (req.Amount <= 0) return Results.Problem("Valor deve ser maior que zero.", statusCode: StatusCodes.Status400BadRequest);

            var t = new Transaction(req.CompanyId, req.UnitId, req.CostCenterId, req.Type,
                req.Description, req.Counterparty, req.Amount, req.DueDate);
            t.ApplyAutoApproval(cc.AutoApprovalLimit);

            db.Transactions.Add(t);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/transactions/{t.Id}", t);
        }).WithName("CreateTransaction");

        group.MapPost("/{id:guid}/approve", async (Guid id, FinanceContext db, CancellationToken ct) =>
            await Transition(db, id, l => l.Approve(), ct)).WithName("ApproveTransaction");

        group.MapPost("/{id:guid}/reject", async (Guid id, RejectRequest req, FinanceContext db, CancellationToken ct) =>
            await Transition(db, id, l => l.Reject(req.Reason), ct)).WithName("RejectTransaction");

        group.MapPost("/{id:guid}/pay", async (Guid id, FinanceContext db, CancellationToken ct) =>
        {
            var t = await db.Transactions.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t is null) return Results.NotFound();
            try { t.MarkAsPaid(); }
            catch (InvalidOperationException ex) { return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict); }
            await db.SaveChangesAsync(ct);
            return Results.Ok(t);
        }).WithName("PayTransaction");
    }

    private static async Task<IResult> Transition(FinanceContext db, Guid id, Action<Transaction> action, CancellationToken ct)
    {
        var t = await db.Transactions.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return Results.NotFound();
        action(t);
        await db.SaveChangesAsync(ct);
        return Results.Ok(t);
    }
}
