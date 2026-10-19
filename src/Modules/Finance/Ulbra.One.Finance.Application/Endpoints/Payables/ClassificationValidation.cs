using Microsoft.EntityFrameworkCore;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;

namespace Ulbra.One.Finance.Application.Endpoints.Payables;

/// <summary>Shared validation for the ledger-account id used to classify a payable. Returns a
/// Problem-Details code (suitable for the <c>title</c>) when the supplied id is invalid, or null otherwise.</summary>
internal static class ClassificationValidation
{
    public static async Task<string?> ValidateAsync(IUnitOfWork uow, Guid? accId, CancellationToken ct)
    {
        if (accId is not { } id) return null;
        var account = await uow.ReadSet<LedgerAccount>().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (account is null) return ErrorCodes.LedgerAccount.NotFound;
        if (!account.CanReceiveEntry) return ErrorCodes.LedgerAccount.NotPostable;
        return null;
    }
}
