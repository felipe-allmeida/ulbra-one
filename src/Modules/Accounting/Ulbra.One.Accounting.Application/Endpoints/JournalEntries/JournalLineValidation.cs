using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Accounting.Application.CostCenters;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Application.Persistence;

namespace Ulbra.One.Accounting.Application.Endpoints.JournalEntries;

/// <summary>Shared validation of the I/O-dependent properties of a draft journal entry's lines:
/// every line must reference an active analytical account (the chart of accounts is global). The
/// structural invariants (≥2 lines, positive amounts, balanced) are NOT checked here — they are owned
/// by the domain (<see cref="JournalEntry.ValidateStructure"/>), the single source of truth, called by
/// the create/update endpoints after building the aggregate (and again at Post time).</summary>
internal static class JournalLineValidation
{
    /// <summary>Returns a Problem result on the first I/O violation, or null when every referenced
    /// account is valid.</summary>
    public static async Task<IResult?> ValidateAsync(
        IReadOnlyList<JournalLineInput> lines, IUnitOfWork uow, CancellationToken ct)
    {
        var accountIds = (lines ?? []).Select(l => l.LedgerAccountId.Value).Distinct().ToList();
        var accounts = await uow.ReadSet<LedgerAccount>()
            .Where(a => accountIds.Contains(a.Id))
            .ToListAsync(ct);
        var byId = accounts.ToDictionary(a => a.Id);

        foreach (var accountId in accountIds)
        {
            if (!byId.TryGetValue(accountId, out var account))
                return TypedResults.Problem(detail: $"Ledger account not found.", statusCode: 400, title: "JournalEntry.AccountNotFound");
            if (!account.CanReceiveEntry)
                return TypedResults.Problem(detail: "Lines may only reference active analytical accounts.", statusCode: 400, title: "JournalEntry.NonPostableAccount");
        }

        return null;
    }

    /// <summary>Validates each line's cost center (when present) against Core: it must exist, be active,
    /// and belong to the entry's company. Returns a Problem on the first violation, else null. Cost center
    /// is referenced by loose Guid (no FK), so this cross-module check is the integrity guard.</summary>
    public static async Task<IResult?> ValidateCostCentersAsync(
        IReadOnlyList<JournalLineInput> lines, Guid companyId, ICostCenterLookup lookup, CancellationToken ct)
    {
        var ids = (lines ?? [])
            .Where(l => l.CostCenterId is not null)
            .Select(l => l.CostCenterId!.Value.Value)
            .Distinct()
            .ToList();
        if (ids.Count == 0) return null;

        var byId = await lookup.GetAsync(ids, ct);
        foreach (var id in ids)
        {
            if (!byId.TryGetValue(id, out var cc))
                return TypedResults.Problem(detail: "Cost center not found.", statusCode: 400, title: "JournalEntry.CostCenterNotFound");
            if (!cc.Active)
                return TypedResults.Problem(detail: "Cost center is inactive.", statusCode: 400, title: "JournalEntry.CostCenterInactive");
            if (cc.CompanyId != companyId)
                return TypedResults.Problem(detail: "Cost center belongs to another company.", statusCode: 400, title: "JournalEntry.CostCenterCompanyMismatch");
        }

        return null;
    }

    /// <summary>Builds the domain lines from the wire inputs, mapping the <see cref="JournalLine"/>
    /// ctor's non-positive-amount rejection 1:1 to the domain's stable
    /// <see cref="JournalEntryErrors.NonPositiveAmount"/> code. On success returns the built lines via
    /// <paramref name="lines"/> and a null result.</summary>
    public static IResult? TryBuildLines(
        IReadOnlyList<JournalLineInput> inputs, out List<JournalLine> lines)
    {
        lines = [];
        try
        {
            foreach (var input in inputs ?? [])
                lines.Add(new JournalLine(input.LedgerAccountId.Value, input.Side, input.Amount,
                    input.CostCenterId?.Value, input.UnitId?.Value, input.Description));
        }
        catch (ArgumentException)
        {
            return TypedResults.Problem(detail: JournalEntryErrors.NonPositiveAmount.Description,
                statusCode: StatusCodes.Status400BadRequest, title: JournalEntryErrors.NonPositiveAmount.Code);
        }
        return null;
    }

    /// <summary>Runs the domain's draft-composition invariant (≥2 lines) on a built draft, mapping a
    /// violation 1:1 to a Problem result with the domain's stable code (TooFewLines). Returns null when
    /// valid. Balancing is intentionally NOT enforced for a draft — it is a Post-time rule. Single
    /// source of truth: delegates to <see cref="JournalEntry.ValidateDraftComposition"/>, which shares
    /// its codes with Post.</summary>
    public static IResult? ValidateDraftComposition(JournalEntry entry)
    {
        try { entry.ValidateDraftComposition(); }
        catch (JournalEntryException ex)
        {
            return TypedResults.Problem(detail: ex.Error.Description,
                statusCode: StatusCodes.Status400BadRequest, title: ex.Error.Code);
        }
        return null;
    }
}
