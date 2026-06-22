using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Accounting.Application.Endpoints.JournalEntries;

/// <summary>Shared validation of the accounts referenced by a draft journal entry's lines:
/// at least two lines, positive amounts, and every line referencing an active analytical account
/// of the same company. Domain balancing is enforced separately at Post time.</summary>
internal static class JournalLineValidation
{
    /// <summary>Returns a Problem result on the first violation, or null when the lines are valid.</summary>
    public static async Task<IResult?> ValidateAsync(
        Guid companyId, IReadOnlyList<JournalLineInput> lines, IUnitOfWork uow, CancellationToken ct)
    {
        if (lines is null || lines.Count < 2)
            return TypedResults.Problem(detail: "A journal entry needs at least two lines.", statusCode: 400, title: "JournalEntry.TooFewLines");

        if (lines.Any(l => l.Amount <= 0))
            return TypedResults.Problem(detail: "All line amounts must be positive.", statusCode: 400, title: "JournalEntry.InvalidAmount");

        var accountIds = lines.Select(l => l.LedgerAccountId.Value).Distinct().ToList();
        var accounts = await uow.ReadSet<LedgerAccount>()
            .Where(a => accountIds.Contains(a.Id))
            .ToListAsync(ct);
        var byId = accounts.ToDictionary(a => a.Id);

        foreach (var accountId in accountIds)
        {
            if (!byId.TryGetValue(accountId, out var account))
                return TypedResults.Problem(detail: $"Ledger account not found.", statusCode: 400, title: "JournalEntry.AccountNotFound");
            if (account.CompanyId != companyId)
                return TypedResults.Problem(detail: "All lines must reference accounts of the entry's company.", statusCode: 400, title: "JournalEntry.CrossCompanyAccount");
            if (!account.CanReceiveEntry)
                return TypedResults.Problem(detail: "Lines may only reference active analytical accounts.", statusCode: 400, title: "JournalEntry.NonPostableAccount");
        }

        return null;
    }
}
