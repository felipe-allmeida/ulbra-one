using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Accounting.Application.Contracts;
using Ulbra.One.Accounting.Application.Seeding;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Accounting.Application.Endpoints.LedgerAccounts.SeedLedgerAccounts;

/// <summary>Seeds the global AELBRA chart of accounts (the single canonical plano de contas, shared by all companies).
/// Idempotent: a no-op (returns the existing chart) when any ledger accounts already exist.</summary>
public sealed class SeedLedgerAccounts : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("/seed", Handle).WithName("SeedLedgerAccounts").WithTags("Accounting")
            .WithSummary("Seeds the standard chart of accounts.")
            .WithDescription("Populates the global AELBRA chart in one call, for bootstrapping an empty "
                + "environment. Idempotent by absence, not by merge: if ANY ledger account already exists "
                + "it does nothing and returns the current chart with 200 instead of 201. It will not top "
                + "up a partially-filled chart, so a missing account has to be created individually.")
            .RequirePermission(Permissions.AccountingWrite)
            .Produces<List<LedgerAccountResponse>>(StatusCodes.Status201Created)
            .Produces<List<LedgerAccountResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

    private static async Task<IResult> Handle(IUnitOfWork uow, CancellationToken ct)
    {
        var existing = await uow.ReadSet<LedgerAccount>()
            .OrderBy(a => a.Code)
            .ToListAsync(ct);
        if (existing.Count > 0)
            return TypedResults.Ok(existing.Select(LedgerAccountResponse.From).ToList());

        var accounts = AelbraChart.Build();
        foreach (var account in accounts)
            await uow.AddAsync(account, ct);
        await uow.SaveChangesAsync(ct);

        var body = accounts.OrderBy(a => a.Code).Select(LedgerAccountResponse.From).ToList();
        return TypedResults.Created("/api/ledger-accounts", body);
    }
}
