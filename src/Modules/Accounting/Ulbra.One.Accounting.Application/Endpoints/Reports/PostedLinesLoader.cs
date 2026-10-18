using Microsoft.EntityFrameworkCore;
using Ulbra.One.Accounting.Application.Reports;
using Ulbra.One.Accounting.Domain;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Accounting.Application.Endpoints.Reports;

/// <summary>Loads the flattened <see cref="PostedLine"/> input the report calculators consume: every
/// line of a scriptured (Posted or Reversed) journal entry joined with its account's metadata. Optionally
/// scoped to a company (omitted = consolidated across all companies). Reversed entries are included so a
/// reversal nets against its original (the mirror is Posted, the original is Reversed): each stays in its
/// own competence, so a closed period reprinted after a later reversal is unchanged.</summary>
internal static class PostedLinesLoader
{
    public static async Task<List<PostedLine>> LoadAsync(
        IUnitOfWork uow, Guid? companyId, CancellationToken ct, Guid? costCenterId = null, Guid? unitId = null)
    {
        var entries = uow.ReadSet<JournalEntry>()
            .Where(e => e.Status == JournalStatus.Posted || e.Status == JournalStatus.Reversed);
        if (companyId is { } cid) entries = entries.Where(e => e.CompanyId == cid);

        // Join posted lines to their accounts in SQL, projecting only the calculator's primitive inputs.
        // Unit filter mirrors cost center: an analytical view of the lines tagged with that dimension.
        var query =
            from e in entries
            from l in e.Lines
            join a in uow.ReadSet<LedgerAccount>() on l.LedgerAccountId equals a.Id
            where (costCenterId == null || l.CostCenterId == costCenterId)
               && (unitId == null || l.UnitId == unitId)
            select new PostedLine(
                a.Id, a.Code, a.Name, a.AccountGroup, a.NormalBalance, l.Side, l.Amount, e.CompetenceDate,
                l.CostCenterId, l.UnitId, a.ReducedCode, a.DreLine, e.Origin);

        return await query.ToListAsync(ct);
    }
}
