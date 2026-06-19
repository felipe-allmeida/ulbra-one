using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.CashFlow;

/// <summary>Sets the reconciliation anchor (real balance) of a bank account — the only cash-flow write.</summary>
public sealed class SetReconciledBalance : IEndpoint
{
    public sealed record Request(decimal Balance, DateOnly AsOf);

    public sealed record SetReconciledBalanceResponse(
        BankAccountId Id, decimal ReconciledBalance, DateOnly ReconciledAt, decimal CurrentBalance);

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPut("/{id}/reconciled-balance", Handle).WithName("SetReconciledBalance").WithTags("Finance")
            .RequireAuthorization()
            .Produces<SetReconciledBalanceResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(
        BankAccountId id, Request req, IUnitOfWork uow, CancellationToken ct)
    {
        var account = await uow.Set<BankAccount>().FirstOrDefaultAsync(a => a.Id == id.Value, ct);
        if (account is null)
            return TypedResults.Problem(detail: "Bank account not found.", statusCode: 404, title: "BankAccount.NotFound");

        account.SetReconciledBalance(req.Balance, req.AsOf);
        await uow.SaveChangesAsync(ct);

        // Recompute current balance the same way the report does: anchor + movements after the anchor date.
        var (accounts, _) = await CashFlowQueries.AccountBalancesAsync(uow, account.CompanyId, ct);
        var current = accounts.FirstOrDefault(a => a.BankAccountId.Value == account.Id)?.CurrentBalance ?? req.Balance;

        return TypedResults.Ok(new SetReconciledBalanceResponse(id, req.Balance, req.AsOf, current));
    }
}
