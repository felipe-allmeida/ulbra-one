using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
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
    public sealed record Request(CompanyId CompanyId, BankAccountId BankAccountId, DateOnly Date);
    public sealed record Response(int Ignored);

    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapPost("clear", Handle).WithName("ClearReconciliation").WithTags("Finance")
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
