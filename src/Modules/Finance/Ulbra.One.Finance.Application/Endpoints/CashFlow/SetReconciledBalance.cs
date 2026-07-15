using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
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
            .RequirePermission(Permissions.TreasuryWrite)
            .Produces<SetReconciledBalanceResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Handle(
        BankAccountId id, Request req, IUnitOfWork uow, TimeProvider clock, CancellationToken ct)
    {
        // A negative balance is intentionally allowed (overdraft / cheque especial) — OpeningBalance
        // permits it too. Only the date is constrained: the anchor cannot be in the future, otherwise
        // today's derived balance (and every report fed by it) would be wrong.
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        if (req.AsOf > today)
            return TypedResults.Problem(detail: "Reconciliation date cannot be in the future.", statusCode: 400, title: "BankAccount.ReconciledAtInvalid");

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
