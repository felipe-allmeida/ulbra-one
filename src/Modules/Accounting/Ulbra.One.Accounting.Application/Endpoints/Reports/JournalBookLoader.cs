using Microsoft.EntityFrameworkCore;
using Ulbra.One.Accounting.Application.Reports;
using Ulbra.One.Accounting.Domain;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Accounting.Application.Endpoints.Reports;

/// <summary>Loads the flat posted/reversed lines (joined to account labels) for the Livro Diário, scoped by
/// company and competence range. Drafts are excluded — the book shows only scriptured entries.</summary>
internal static class JournalBookLoader
{
    public static async Task<List<PostedEntryLine>> LoadAsync(
        IUnitOfWork uow, Guid? companyId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var entries = uow.ReadSet<JournalEntry>()
            .Where(e => e.Status == JournalStatus.Posted || e.Status == JournalStatus.Reversed);
        if (companyId is { } cid) entries = entries.Where(e => e.CompanyId == cid);
        if (from is { } f) entries = entries.Where(e => e.CompetenceDate >= f);
        if (to is { } t) entries = entries.Where(e => e.CompetenceDate <= t);

        var query =
            from e in entries
            from l in e.Lines
            join a in uow.ReadSet<LedgerAccount>() on l.LedgerAccountId equals a.Id
            select new PostedEntryLine(
                e.Id, e.Number, e.CompetenceDate, e.Description, e.Status,
                a.Id, a.Code, a.Name, l.Side, l.Amount, l.CostCenterId);

        return await query.ToListAsync(ct);
    }
}
