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
using Ulbra.One.Common.Application.Pagination;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Conciliation;
using Ulbra.One.Finance.Application.Contracts;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Conciliation.ListBankTransactions;

/// <summary>Query-string filters for the reconciliation working list. Bound via <c>[AsParameters]</c>.</summary>
public sealed record ListBankTransactionsRequest : PaginationQueryParameters
{
    [SwaggerSchema("Company that owns the bank account. Omit to list across every company.")]
    public CompanyId? CompanyId { get; set; }

    [SwaggerSchema("Bank account whose statement lines to list.")]
    public BankAccountId? BankAccountId { get; set; }

    [SwaggerSchema("Restrict to the lines that came from one statement import.")]
    public StatementImportId? ImportId { get; set; }

    [SwaggerSchema("Reconciliation status to keep: unmatched, suggested, reconciled or ignored.")]
    public MatchStatus? MatchStatus { get; set; }

    [SwaggerSchema("Screen stage to keep — the grouping the reconciliation tabs use on top of the raw "
        + "match status.")]
    public string? Stage { get; set; }

    [SwaggerSchema("Keep only debits or only credits.")]
    public TransactionDirection? Direction { get; set; }

    [SwaggerSchema("Keep lines dated on or after this day.")]
    [SwaggerExample("2026-09-01")]
    public DateOnly? DueFrom { get; set; }

    [SwaggerSchema("Keep lines dated on or before this day.")]
    [SwaggerExample("2026-09-30")]
    public DateOnly? DueTo { get; set; }
}

public sealed class ListBankTransactions : IEndpoint
{
    public void Map(IEndpointRouteBuilder builder) =>
        builder.MapGet("", Handle).WithName("ListBankTransactions").WithTags("Finance")
            .WithSummary("Lists bank transactions to reconcile.")
            .WithDescription("The statement lines and where each one stands: unmatched, suggested, reconciled or ignored. "
                + "This is the working list of the reconciliation screen. Still-open lines also carry Mismatch and "
                + "Candidates — why the line could not be paired and which installments come closest — computed "
                + "against the pool AS IT STANDS NOW, so they can disagree with the verdict the import reached. "
                + "Reconciled and ignored lines carry neither, and no other endpoint returns them.")
            .RequirePermission(Permissions.TreasuryRead).Produces<PaginatedResponse<BankTransactionResponse>>(StatusCodes.Status200OK);

    private static async Task<IResult> Handle(
        IUnitOfWork uow,
        [AsParameters] ListBankTransactionsRequest req, CancellationToken ct)
    {
        var (companyId, bankAccountId, importId, matchStatus, stage, direction, dueFrom, dueTo) =
            (req.CompanyId, req.BankAccountId, req.ImportId, req.MatchStatus, req.Stage, req.Direction,
             req.DueFrom, req.DueTo);
        var q = uow.ReadSet<BankTransaction>();
        if (companyId is { } c) q = q.Where(t => t.CompanyId == c.Value);
        if (bankAccountId is { } b) q = q.Where(t => t.BankAccountId == b.Value);
        if (importId is { } im) q = q.Where(t => t.ImportId == im.Value);
        if (matchStatus is { } ms) q = q.Where(t => t.MatchStatus == ms);
        // Stage groups the statuses into the two review tabs: "analysis" = still open (import lands here),
        // "resolved" = already treated (approved or ignored).
        if (string.Equals(stage, "analysis", StringComparison.OrdinalIgnoreCase))
            q = q.Where(t => t.MatchStatus == MatchStatus.Unmatched || t.MatchStatus == MatchStatus.Suggested);
        else if (string.Equals(stage, "resolved", StringComparison.OrdinalIgnoreCase))
            q = q.Where(t => t.MatchStatus == MatchStatus.Reconciled || t.MatchStatus == MatchStatus.Ignored);
        if (direction is { } dir) q = q.Where(t => t.Direction == dir);
        if (dueFrom is { } from) q = q.Where(t => t.Date >= from);
        if (dueTo is { } to) q = q.Where(t => t.Date <= to);
        // Deterministic, newest-first order. Date alone ties on single-day statements (every line same
        // DTPOSTED), which makes OFFSET pagination drop/duplicate rows — CreatedAt + Id break the tie.
        var page = await q
            .OrderByDescending(t => t.Date)
            .ThenByDescending(t => t.CreatedAt)
            .ThenBy(t => t.Id)
            .ToPaginatedAsync(req, ct);

        var explained = await ExplainAsync(uow, page.Items, ct);
        var paired = await PairAsync(uow, page.Items, explained, ct);
        return TypedResults.Ok(page.Map(t => paired[t.Id]));
    }

    /// <summary>
    /// Attaches the título behind each RECONCILED line, so the screen can show the pairing — conta a pagar ×
    /// extrato — instead of only asserting that the line was reconciled. One lookup by payment id for the whole
    /// page. Ignored lines are skipped: there is no título behind them, which is the point of ignoring one.
    /// </summary>
    private static async Task<Dictionary<Guid, BankTransactionResponse>> PairAsync(
        IUnitOfWork uow, IReadOnlyList<BankTransaction> rows,
        Dictionary<Guid, BankTransactionResponse> current, CancellationToken ct)
    {
        var paymentIds = rows
            .Where(t => t.MatchStatus == MatchStatus.Reconciled && t.MatchedPaymentId is not null)
            .Select(t => t.MatchedPaymentId!.Value)
            .Distinct()
            .ToList();
        if (paymentIds.Count == 0) return current;

        var titles = await (
            from p in uow.ReadSet<Payable>()
            from i in p.Installments
            from pay in i.Payments
            where paymentIds.Contains(pay.Id)
            select new
            {
                PaymentId = pay.Id,
                p.Id,
                p.CounterpartyName,
                p.DocumentNumber,
                i.Sequence,
                InstallmentCount = p.Installments.Count,
                i.DueDate,
                pay.Amount,
                pay.PaymentDate,
                pay.Notes,
            })
            .ToListAsync(ct);
        var byPayment = titles.ToDictionary(x => x.PaymentId);

        foreach (var row in rows)
        {
            if (row.MatchStatus != MatchStatus.Reconciled || row.MatchedPaymentId is not { } pid) continue;
            if (!byPayment.TryGetValue(pid, out var title)) continue;
            current[row.Id] = current[row.Id] with
            {
                MatchedTitle = new ConciliationMatchedTitleResponse(
                    new PayableId(title.Id), title.CounterpartyName, title.DocumentNumber,
                    title.Sequence, title.InstallmentCount, title.DueDate,
                    title.Amount, title.PaymentDate, title.Notes),
            };
        }

        return current;
    }

    /// <summary>An open installment of the page's companies, with the payable fields the screen renders.</summary>
    private sealed record PoolEntry(
        Guid InstallmentId, decimal Remaining, DateOnly DueDate, CandidateDisplay Display);

    /// <summary>
    /// Reads why each still-open line on the page is where it is, and which installments come closest.
    /// <para>
    /// One extra query for the near-miss pool, bounded to the page's companies and to a date band around the
    /// page's own rows, plus one lookup for the installments the matcher already suggested — those are fetched by
    /// id rather than taken from the pool, because a suggested parcela may have moved out of it since the import.
    /// Reconciled and ignored lines are left alone: there is nothing to explain about a line already treated.
    /// </para>
    /// </summary>
    private static async Task<Dictionary<Guid, BankTransactionResponse>> ExplainAsync(
        IUnitOfWork uow, IReadOnlyList<BankTransaction> rows, CancellationToken ct)
    {
        var open = rows
            .Where(t => t.MatchStatus is MatchStatus.Unmatched or MatchStatus.Suggested)
            .ToList();
        if (open.Count == 0)
            return rows.ToDictionary(t => t.Id, BankTransactionResponse.From);

        var companyIds = open.Select(t => t.CompanyId).Distinct().ToList();
        var poolFrom = open.Min(t => t.Date).AddDays(-ConciliationMatcher.NearWindowDays);
        var poolTo = open.Max(t => t.Date).AddDays(ConciliationMatcher.NearWindowDays);

        var pool = await (
            from p in uow.ReadSet<Payable>()
            where companyIds.Contains(p.CompanyId)
                && (p.Status == PayableStatus.Scheduled || p.Status == PayableStatus.Paid)
            from i in p.Installments
            where (i.Status == InstallmentStatus.Open || i.Status == InstallmentStatus.PartiallyPaid)
                && i.DueDate >= poolFrom && i.DueDate <= poolTo
            select new PoolEntry(i.Id, i.NetAmount - i.PaidAmount, i.DueDate,
                new CandidateDisplay(p.Id, p.CounterpartyName, p.DocumentNumber, i.Sequence, p.Installments.Count)))
            .ToListAsync(ct);

        var suggestedIds = open
            .Where(t => t.SuggestedInstallmentId != null)
            .Select(t => t.SuggestedInstallmentId!.Value)
            .Distinct()
            .ToList();
        var suggested = suggestedIds.Count == 0
            ? []
            : await (
                from p in uow.ReadSet<Payable>()
                from i in p.Installments
                where suggestedIds.Contains(i.Id)
                select new PoolEntry(i.Id, i.NetAmount - i.PaidAmount, i.DueDate,
                    new CandidateDisplay(p.Id, p.CounterpartyName, p.DocumentNumber, i.Sequence, p.Installments.Count)))
                .ToListAsync(ct);

        var display = pool.Concat(suggested)
            .GroupBy(e => e.InstallmentId)
            .ToDictionary(g => g.Key, g => g.First());
        var candidates = pool
            .Select(e => new InstallmentCandidate(e.InstallmentId, e.Remaining, e.DueDate))
            .ToList();

        var byId = rows.ToDictionary(t => t.Id, BankTransactionResponse.From);
        foreach (var t in open)
        {
            // A suggestion is not a mismatch: show the parcela the matcher picked and let the operator confirm it.
            if (t.SuggestedInstallmentId is { } sid)
            {
                if (display.TryGetValue(sid, out var e))
                {
                    var near = new CandidateNearMiss(
                        e.InstallmentId, e.Remaining, e.DueDate,
                        Math.Abs(e.Remaining - Math.Abs(t.Amount)),
                        Math.Abs(e.DueDate.DayNumber - t.Date.DayNumber));
                    byId[t.Id] = BankTransactionResponse.From(
                        t, null, [ConciliationCandidateResponse.From(near, e.Display)]);
                }
                continue;
            }

            // Automatic matching only ever runs debits against payables, so a credit is unmatched by design
            // rather than by failure — saying so beats showing an operator an empty candidate list.
            if (t.Direction == TransactionDirection.Credit)
            {
                byId[t.Id] = BankTransactionResponse.From(t, ConciliationMismatch.CreditNotMatched, null);
                continue;
            }

            var explanation = ConciliationMatcher.Explain(t.Amount, t.Date, candidates);
            byId[t.Id] = BankTransactionResponse.From(
                t,
                explanation.Mismatch,
                [.. explanation.Candidates.Select(c => ConciliationCandidateResponse.From(c, display[c.InstallmentId].Display))]);
        }

        return byId;
    }
}
