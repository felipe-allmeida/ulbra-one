using Microsoft.EntityFrameworkCore;
using Ulbra.One.Accounting.Application.Reports;
using Ulbra.One.Accounting.Domain;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Accounting.Application.Endpoints.Reports;

/// <summary>Loads the flat scriptured lines (joined to account labels) for the Livro Razão. Same universe as
/// the Balancete (<see cref="PostedLinesLoader"/>) — Posted and Reversed entries — so a Razão closing balance
/// agrees with the Balancete and a reversal nets against its original in each entry's own competence.
/// Optionally scoped by company, a set of accounts (the current page), and cost center. Cuts only the UPPER
/// competence bound <paramref name="to"/>: the lower bound is left to <see cref="GeneralLedgerBuilder"/>,
/// which needs the movements before <c>from</c> to compute the opening balance.</summary>
internal static class GeneralLedgerLoader
{
    public static async Task<List<GeneralLedgerPostedLine>> LoadAsync(
        IUnitOfWork uow, Guid? companyId, IReadOnlyCollection<Guid>? ledgerAccountIds,
        Guid? costCenterId, DateOnly? to, CancellationToken ct, Guid? unitId = null)
    {
        var entries = uow.ReadSet<JournalEntry>()
            .Where(e => e.Status == JournalStatus.Posted || e.Status == JournalStatus.Reversed);
        if (companyId is { } cid) entries = entries.Where(e => e.CompanyId == cid);
        if (to is { } t) entries = entries.Where(e => e.CompetenceDate <= t);

        var ids = ledgerAccountIds?.ToArray();

        var query =
            from e in entries
            from l in e.Lines
            join a in uow.ReadSet<LedgerAccount>() on l.LedgerAccountId equals a.Id
            where (ids == null || ids.Contains(a.Id))
               && (costCenterId == null || l.CostCenterId == costCenterId)
               && (unitId == null || l.UnitId == unitId)
            select new GeneralLedgerPostedLine(
                a.Id, a.Code, a.ReducedCode, a.Name, a.NormalBalance,
                e.Id, e.Number, e.CompetenceDate, e.Description, e.SourceReference,
                l.Side, l.Amount, l.CostCenterId, l.Description,
                e.CounterpartyName, e.CounterpartyDocument);

        return await query.ToListAsync(ct);
    }
}
