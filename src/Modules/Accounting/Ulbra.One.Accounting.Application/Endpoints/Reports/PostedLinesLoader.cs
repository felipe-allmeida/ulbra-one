using Microsoft.EntityFrameworkCore;
using Ulbra.One.Accounting.Application.Reports;
using Ulbra.One.Accounting.Domain;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Accounting.Application.Endpoints.Reports;

/// <summary>Loads the flattened <see cref="PostedLine"/> input the report calculators consume: every
/// line of a Posted journal entry joined with its account's metadata. Optionally scoped to a company
/// (omitted = consolidated across all companies).</summary>
internal static class PostedLinesLoader
{
    public static async Task<List<PostedLine>> LoadAsync(
        IUnitOfWork uow, Guid? companyId, CancellationToken ct, Guid? costCenterId = null)
    {
        var entries = uow.ReadSet<JournalEntry>().Where(e => e.Status == JournalStatus.Posted);
        if (companyId is { } cid) entries = entries.Where(e => e.CompanyId == cid);

        // Join posted lines to their accounts in SQL, projecting only the calculator's primitive inputs.
        var query =
            from e in entries
            from l in e.Lines
            join a in uow.ReadSet<LedgerAccount>() on l.LedgerAccountId equals a.Id
            where costCenterId == null || l.CostCenterId == costCenterId
            select new PostedLine(
                a.Id, a.Code, a.Name, a.AccountGroup, a.NormalBalance, l.Side, l.Amount, e.CompetenceDate,
                l.CostCenterId, l.UnitId);

        return await query.ToListAsync(ct);
    }
}
