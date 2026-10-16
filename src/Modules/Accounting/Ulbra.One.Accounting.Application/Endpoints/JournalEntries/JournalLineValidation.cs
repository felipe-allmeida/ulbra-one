using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Ulbra.One.Accounting.Application.CostCenters;
using Ulbra.One.Accounting.Application.Units;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Application.Errors;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;

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
                return ErrorResults.InvalidRequest($"Ledger account not found.", ErrorCodes.JournalEntry.AccountNotFound);
            if (!account.CanReceiveEntry)
                return ErrorResults.InvalidRequest("Lines may only reference active analytical accounts.", ErrorCodes.JournalEntry.NonPostableAccount);
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
                return ErrorResults.InvalidRequest("Cost center not found.", ErrorCodes.JournalEntry.CostCenterNotFound);
            if (!cc.Active)
                return ErrorResults.InvalidRequest("Cost center is inactive.", ErrorCodes.JournalEntry.CostCenterInactive);
            if (cc.CompanyId != companyId)
                return ErrorResults.InvalidRequest("Cost center belongs to another company.", ErrorCodes.JournalEntry.CostCenterCompanyMismatch);
        }

        return null;
    }

    /// <summary>Validates each line's unit (when present) against Core: it must exist, be active, and belong to
    /// the entry's company. Returns a Problem on the first violation, else null. Unit is referenced by loose Guid
    /// (no FK), so this cross-module check is the integrity guard — mirrors <see cref="ValidateCostCentersAsync"/>.</summary>
    public static async Task<IResult?> ValidateUnitsAsync(
        IReadOnlyList<JournalLineInput> lines, Guid companyId, IUnitLookup lookup, CancellationToken ct)
    {
        var ids = (lines ?? [])
            .Where(l => l.UnitId is not null)
            .Select(l => l.UnitId!.Value.Value)
            .Distinct()
            .ToList();
        if (ids.Count == 0) return null;

        var byId = await lookup.GetAsync(ids, ct);
        foreach (var id in ids)
        {
            if (!byId.TryGetValue(id, out var u))
                return ErrorResults.InvalidRequest("Unit not found.", ErrorCodes.JournalEntry.UnitNotFound);
            if (!u.Active)
                return ErrorResults.InvalidRequest("Unit is inactive.", ErrorCodes.JournalEntry.UnitInactive);
            if (u.CompanyId != companyId)
                return ErrorResults.InvalidRequest("Unit belongs to another company.", ErrorCodes.JournalEntry.UnitCompanyMismatch);
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
            return ErrorResults.InvalidRequest(JournalEntryErrors.NonPositiveAmount.Description, JournalEntryErrors.NonPositiveAmount.Code);
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
            return ErrorResults.InvalidRequest(ex.Error.Description, ex.Error.Code);
        }
        return null;
    }
}
