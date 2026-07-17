using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application;
using Ulbra.One.Common.Application.Authentication;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Conciliation.GetPeriodSummary;

/// <summary>
/// Aggregates the bank transactions of a period by reconciliation status (conciliado × pendente × ignorado)
/// with per-status count/total plus debit/credit totals — the "pagamentos do período" summary strip.
/// </summary>
public sealed class GetPeriodSummary : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("period-summary", Handle).WithName("GetPeriodSummary").WithTags("Finance")
            .RequirePermission(Permissions.TreasuryRead).Produces<PeriodSummaryResponse>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(
        IUnitOfWork uow,
        CompanyId? companyId, BankAccountId? bankAccountId, TransactionDirection? direction,
        DateOnly? dueFrom, DateOnly? dueTo, CancellationToken ct)
    {
        var q = uow.ReadSet<BankTransaction>();
        if (companyId is { } c) q = q.Where(t => t.CompanyId == c.Value);
        if (bankAccountId is { } b) q = q.Where(t => t.BankAccountId == b.Value);
        if (direction is { } dir) q = q.Where(t => t.Direction == dir);
        if (dueFrom is { } from) q = q.Where(t => t.Date >= from);
        if (dueTo is { } to) q = q.Where(t => t.Date <= to);

        var byStatus = await q
            .GroupBy(t => t.MatchStatus)
            .Select(g => new { Status = g.Key, Count = g.Count(), Total = g.Sum(x => x.Amount) })
            .ToListAsync(ct);

        var byDirection = await q
            .GroupBy(t => t.Direction)
            .Select(g => new { Direction = g.Key, Total = g.Sum(x => x.Amount) })
            .ToListAsync(ct);

        StatusBucket Bucket(MatchStatus s)
        {
            var r = byStatus.FirstOrDefault(x => x.Status == s);
            return r is null ? new StatusBucket(0, 0m) : new StatusBucket(r.Count, r.Total);
        }

        decimal DirectionTotal(TransactionDirection d) =>
            byDirection.FirstOrDefault(x => x.Direction == d)?.Total ?? 0m;

        var response = new PeriodSummaryResponse(
            dueFrom, dueTo,
            Bucket(MatchStatus.Reconciled),
            Bucket(MatchStatus.Suggested),
            Bucket(MatchStatus.Unmatched),
            Bucket(MatchStatus.Ignored),
            DirectionTotal(TransactionDirection.Debit),
            DirectionTotal(TransactionDirection.Credit));

        return TypedResults.Ok(response);
    }
}
