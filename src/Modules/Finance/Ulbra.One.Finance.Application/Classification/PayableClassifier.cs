using Microsoft.EntityFrameworkCore;
using Ulbra.One.Accounting.Domain;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Application.Ai;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Finance.Application.Endpoints.Payables.AiClassificationSuggestion;
using Ulbra.One.Finance.Domain.Entities;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Finance.Application.Classification;

public sealed class PayableClassifier(IAiCompletion ai, IUnitOfWork uow) : IPayableClassifier
{
    private const double SuggestThreshold = 0.5;

    public async Task<PayableClassificationResult> ClassifyAsync(Payable payable, CancellationToken ct)
    {
        // Analytical ledger accounts (the chart of accounts is global reference data).
        var accounts = await uow.ReadSet<LedgerAccount>()
            .Where(c => c.Kind == LedgerAccountKind.Analytical && c.IsActive && c.DeletedAt == null)
            .ToListAsync(ct);

        var fiscalDoc = await uow.ReadSet<FiscalDocument>()
            .FirstOrDefaultAsync(d => d.PayableId == payable.Id, ct);

        // Accounts this vendor is registered against (AEL-786), resolved against the active analytical catalog
        // so orphan/inactive/synthetic ids drop out. Used as a prior in the prompt and, when there's exactly
        // one, as a fallback suggestion. The count that matters is AFTER this resolution.
        var associatedIds = await uow.ReadSet<VendorEntity>()
            .Where(v => v.Id == payable.VendorId)
            .Select(v => v.AssociatedLedgerAccountIds)
            .FirstOrDefaultAsync(ct) ?? [];
        var associated = accounts.Where(a => associatedIds.Contains(a.Id)).ToList();

        var prompt = AiClassificationPrompt.Build(
            payable.Description, payable.CounterpartyName, payable.DocumentNumber, payable.TotalAmount,
            accounts.Select(c => (c.Code, c.Name)).ToList(),
            cfops: fiscalDoc?.Cfops,
            ncms: fiscalDoc?.Ncms,
            vendorAssociatedAccounts: associated.Select(a => (a.Code, a.Name)).ToList(),
            lc116Subitem: fiscalDoc?.Lc116Subitem);

        var aiResult = await ai.CompleteAsync<PayableClassificationSuggestion>(prompt, ct: ct);
        if (aiResult.IsSuccess && aiResult.Value.Confidence >= SuggestThreshold)
        {
            var catalog = accounts.Select(c => new AiAccountRef(c.Id, c.Code)).ToList();
            var acc = AiClassificationResolver.Resolve(aiResult.Value, catalog);
            if (acc is not null)
                return new PayableClassificationResult(acc, aiResult.Value.Confidence, aiResult.Value.Rationale, ClassificationSources.Ai);
        }

        // AI wasn't confident. When the vendor is registered against exactly one account, take it as an
        // explicit-intent suggestion. Runs for ANY non-confident AI result — including disabled/none — so it
        // works with the AI provider off (the common mode). Confidence 0 keeps it a suggestion for a human
        // (the consumer never auto-classifies below 0.8), never an automatic booking. Ranked above the history
        // fallback: what the vendor is registered for is a stronger signal than what it was booked to before.
        if (associated.Count == 1)
            return new PayableClassificationResult(associated[0].Id, 0, "Vendor's registered default ledger account.", ClassificationSources.Heuristic);

        var (hAcc, _) = await VendorHistorySuggestion.FindAsync(uow, payable, ct, associatedIds);
        if (hAcc is not null)
            return new PayableClassificationResult(hAcc.Value.Value, 0, "Based on prior classifications for this vendor.", ClassificationSources.Heuristic);

        // The AI *errored* (down or rate-limited/429) and there is no fallback: flag it distinctly so the
        // consumer settles for a human instead of redelivering into a throttled provider (the 429 → retry → 429
        // cost spiral). Only a real provider error counts — the offline/Fake provider (ErrorCodes.Ai.Disabled) is
        // the expected "AI off" mode and stays "none", not "unavailable".
        if (aiResult.IsFailure && aiResult.Error.Code is ErrorCodes.Ai.RateLimited or ErrorCodes.Ai.ProviderError)
            return new PayableClassificationResult(null, 0, "AI provider unavailable.", ClassificationSources.Unavailable);

        // AI answered but produced nothing usable (or is disabled): genuinely no signal for this payable.
        return new PayableClassificationResult(null, 0, "No suggestion available.", "none");
    }
}
