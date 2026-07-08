using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.AiClassificationSuggestion;

/// <summary>Heuristic suggestion: the most recent classified payable for the same vendor,
/// preferring the same cost center. Shared by the heuristic and AI-fallback endpoints.</summary>
public static class VendorHistorySuggestion
{
    public static async Task<(LedgerAccountId? Accounting, PayableId? BasedOn)> FindAsync(
        IUnitOfWork uow, Payable target, CancellationToken ct)
    {
        var match = await uow.ReadSet<Payable>()
            .Where(p => p.Id != target.Id && p.VendorId == target.VendorId
                        && p.AccountingAccountId != null)
            .OrderByDescending(p => p.CostCenterId == target.CostCenterId)
            .ThenByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (match is null) return (null, null);
        return (new LedgerAccountId(match.AccountingAccountId!.Value), new PayableId(match.Id));
    }
}
