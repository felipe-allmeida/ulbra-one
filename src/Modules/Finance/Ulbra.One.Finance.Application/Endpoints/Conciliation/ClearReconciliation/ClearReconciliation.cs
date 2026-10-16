using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Conciliation.ClearReconciliation;

/// <summary>
/// Bulk "ignore" for a day: marks every still-open bank transaction (Unmatched or Suggested) of a given
/// account on a given date as Ignored — a batch version of the per-row Ignorar. Reconciled lines are
/// left untouched (they are real settlements).
/// </summary>
public sealed class ClearReconciliation : IEndpoint
{
    public sealed record Request(
        [property: SwaggerSchema("Company that owns the bank account.")]
        CompanyId CompanyId,
        [property: SwaggerSchema("Bank account whose day is being cleared.")]
        BankAccountId BankAccountId,
        [property: SwaggerSchema("The day to clear. Only transactions on this exact date are affected.")]
        [property: SwaggerExample("2026-09-10")]
        DateOnly Date);

    public sealed record Response(
        [property: SwaggerSchema("How many transactions were ignored by this call.")]
        int Ignored);

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("clear", Handle).WithName("ClearReconciliation").WithTags("Finance")
            .WithSummary("Ignores every open transaction of a day.")
            .WithDescription("Bulk version of ignore: marks every still-open line (unmatched or suggested) of one account "
                + "on one day as ignored, and returns how many were affected. Already-reconciled lines are left "
                + "ALONE - they are real settlements, so this cannot be used to undo a reconciliation.")
            .RequirePermission(Permissions.TreasuryWrite)
            .Produces<Response>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(Request req, IUnitOfWork uow, CancellationToken ct)
    {
        var companyId = req.CompanyId.Value;
        var bankAccountId = req.BankAccountId.Value;
        var date = req.Date;

        var transactions = await uow.Set<BankTransaction>()
            .Where(t => t.CompanyId == companyId && t.BankAccountId == bankAccountId && t.Date == date
                && (t.MatchStatus == MatchStatus.Unmatched || t.MatchStatus == MatchStatus.Suggested))
            .ToListAsync(ct);

        foreach (var t in transactions)
            t.Ignore();

        if (transactions.Count > 0)
            await uow.SaveChangesAsync(ct);

        return TypedResults.Ok(new Response(transactions.Count));
    }
}
