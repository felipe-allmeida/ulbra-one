using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.CashFlow;

/// <summary>Sets the reconciliation anchor (real balance) of a bank account — the only cash-flow write.</summary>
public sealed class SetReconciledBalance : IEndpoint
{
    public sealed record Request(
        [property: SwaggerSchema("The real balance of the account on AsOf, in BRL, as confirmed against "
            + "the bank. This becomes the anchor every projection starts from.")]
        [property: SwaggerExample("250000.00")]
        decimal Balance,
        [property: SwaggerSchema("Date the balance refers to.")]
        [property: SwaggerExample("2026-09-01")]
        DateOnly AsOf);

    [SwaggerSchema("The account's balance picture after the reconciliation anchor was set.")]
    public sealed record SetReconciledBalanceResponse(
        BankAccountId Id,
        [property: SwaggerSchema("The balance confirmed against the bank, in BRL — the anchor itself.")]
        [property: SwaggerExample("482350.00")]
        decimal ReconciledBalance,
        [property: SwaggerSchema("The date that balance refers to.")]
        [property: SwaggerExample("2026-09-01")]
        DateOnly ReconciledAt,
        [property: SwaggerSchema("Balance now, in BRL: the anchor plus everything posted since. It is "
            + "what the cash-flow projection starts from.")]
        [property: SwaggerExample("448200.00")]
        decimal CurrentBalance);

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPut("/{id}/reconciled-balance", Handle).WithName("SetReconciledBalance").WithTags("Finance")
            .WithSummary("Sets a bank account\u0027s reconciled balance.")
            .WithDescription("Records the real balance confirmed against the bank on a date - the anchor the cash-flow "
                + "projection starts from, and the only write in the whole cash-flow area. Everything projected "
                + "downstream shifts with it, so a wrong figure here quietly skews every forecast.")
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
            return ErrorResults.InvalidRequest("Reconciliation date cannot be in the future.", ErrorCodes.BankAccount.ReconciledAtInvalid);

        var account = await uow.Set<BankAccount>().FirstOrDefaultAsync(a => a.Id == id.Value, ct);
        if (account is null)
            return ErrorResults.NotFound("Bank account not found.", ErrorCodes.BankAccount.NotFound);

        account.SetReconciledBalance(req.Balance, req.AsOf);
        await uow.SaveChangesAsync(ct);

        // Recompute current balance the same way the report does: anchor + movements after the anchor date.
        var (accounts, _) = await CashFlowQueries.AccountBalancesAsync(uow, account.CompanyId, ct);
        var current = accounts.FirstOrDefault(a => a.BankAccountId.Value == account.Id)?.CurrentBalance ?? req.Balance;

        return TypedResults.Ok(new SetReconciledBalanceResponse(id, req.Balance, req.AsOf, current));
    }
}
