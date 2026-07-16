using Ulbra.One.Accounting.Domain;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Accounting.Application.Endpoints.Reports;

/// <summary>Builds the ordered set of ledger accounts that qualify for the Livro Razão: accounts with at least
/// one Posted line up to <paramref name="to"/> matching the company / cost-center filters, optionally narrowed
/// to a single account or an account root (code prefix "1".."5"). Ordered by code. Shared by the paginated GET
/// and the full CSV export.</summary>
internal static class GeneralLedgerAccountQuery
{
    public static IQueryable<LedgerAccount> Qualifying(
        IUnitOfWork uow, Guid? companyId, Guid? ledgerAccountId, string? root, Guid? costCenterId, DateOnly? to)
    {
        var rootPrefix = string.IsNullOrWhiteSpace(root) ? null : root.Trim() + ".";

        var postedAccountIds =
            from e in uow.ReadSet<JournalEntry>().Where(e => e.Status == JournalStatus.Posted)
            from l in e.Lines
            where (companyId == null || e.CompanyId == companyId)
               && (to == null || e.CompetenceDate <= to)
               && (costCenterId == null || l.CostCenterId == costCenterId)
            select l.LedgerAccountId;

        return
            from a in uow.ReadSet<LedgerAccount>()
            where (ledgerAccountId == null || a.Id == ledgerAccountId)
               && (rootPrefix == null || a.Code.StartsWith(rootPrefix))
               && postedAccountIds.Contains(a.Id)
            orderby a.Code
            select a;
    }
}
