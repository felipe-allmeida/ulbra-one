using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application;
using Ulbra.One.Core.Infrastructure.Database;
using Ulbra.One.Finance.Application.Contracts;
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
            var items = await q.OrderBy(l => l.DueDate).ToListAsync(ct);
            return TypedResults.Ok(items.Select(TransactionResponse.From).ToList());
        })
            .WithName("ListTransactions")
            .Produces<List<TransactionResponse>>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}", async (Guid id, FinanceContext db, CancellationToken ct) =>
            await db.Transactions.FirstOrDefaultAsync(l => l.Id == id, ct) is { } l
                ? Results.Ok(TransactionResponse.From(l)) : Results.NotFound())
            .WithName("GetTransaction")
            .Produces<TransactionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/", async (CreateTransactionRequest req, FinanceContext db, CoreContext core, CancellationToken ct) =>
        {
            var cc = await core.CostCenters.FirstOrDefaultAsync(c => c.Id == req.CostCenterId && c.CompanyId == req.CompanyId, ct);
            if (cc is null) return Results.Problem(detail: "Cost center is invalid for this company.", statusCode: StatusCodes.Status400BadRequest, title: "Transaction.CostCenterInvalid");

            var unitOk = await core.Units.AnyAsync(u => u.Id == req.UnitId && u.CompanyId == req.CompanyId, ct);
            if (!unitOk) return Results.Problem(detail: "Unit is invalid for this company.", statusCode: StatusCodes.Status400BadRequest, title: "Transaction.UnitInvalid");

            if (req.Amount <= 0) return Results.Problem(detail: "Amount must be greater than zero.", statusCode: StatusCodes.Status400BadRequest, title: "Transaction.AmountInvalid");

            var t = new Transaction(req.CompanyId, req.UnitId, req.CostCenterId, req.Type,
                req.Description, req.Counterparty, req.Amount, req.DueDate);
            t.ApplyAutoApproval(cc.AutoApprovalLimit);

            db.Transactions.Add(t);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/transactions/{t.Id}", TransactionResponse.From(t));
        })
            .WithName("CreateTransaction")
            .Produces<TransactionResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/{id:guid}/approve", async (Guid id, FinanceContext db, CancellationToken ct) =>
            await Transition(db, id, l => l.Approve(), ct))
            .WithName("ApproveTransaction")
            .Produces<TransactionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/reject", async (Guid id, RejectRequest req, FinanceContext db, CancellationToken ct) =>
            await Transition(db, id, l => l.Reject(req.Reason), ct))
            .WithName("RejectTransaction")
            .Produces<TransactionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/pay", async (Guid id, FinanceContext db, CancellationToken ct) =>
        {
            var t = await db.Transactions.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t is null) return Results.NotFound();
            try { t.MarkAsPaid(); }
            catch (InvalidOperationException ex) { return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict); }
            await db.SaveChangesAsync(ct);
            return Results.Ok(TransactionResponse.From(t));
        })
            .WithName("PayTransaction")
            .Produces<TransactionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<IResult> Transition(FinanceContext db, Guid id, Action<Transaction> action, CancellationToken ct)
    {
        var t = await db.Transactions.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return Results.NotFound();
        action(t);
        await db.SaveChangesAsync(ct);
        return Results.Ok(TransactionResponse.From(t));
    }
}
