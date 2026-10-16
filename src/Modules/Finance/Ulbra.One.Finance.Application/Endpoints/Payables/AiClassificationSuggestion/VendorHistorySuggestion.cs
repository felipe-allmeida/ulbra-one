using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.AiClassificationSuggestion;

/// <summary>Heuristic suggestion: the most recent classified payable for the same vendor,
/// preferring the same cost center. Shared by the heuristic and AI-fallback endpoints.
/// <paramref name="preferredAccountIds"/> (the vendor's registered accounts, optional) breaks ties toward a
/// past payable booked to one of them.</summary>
public static class VendorHistorySuggestion
{
    public static async Task<(LedgerAccountId? Accounting, PayableId? BasedOn)> FindAsync(
        IUnitOfWork uow, Payable target, CancellationToken ct, IReadOnlyList<Guid>? preferredAccountIds = null)
    {
        var query = uow.ReadSet<Payable>()
            .Where(p => p.Id != target.Id && p.VendorId == target.VendorId && p.AccountingAccountId != null)
            .OrderByDescending(p => p.CostCenterId == target.CostCenterId);

        if (preferredAccountIds is { Count: > 0 })
            query = query.ThenByDescending(p => preferredAccountIds.Contains(p.AccountingAccountId!.Value));

        var match = await query.ThenByDescending(p => p.CreatedAt).FirstOrDefaultAsync(ct);

        if (match is null) return (null, null);
        return (new LedgerAccountId(match.AccountingAccountId!.Value), new PayableId(match.Id));
    }
}
