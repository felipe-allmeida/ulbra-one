using Microsoft.EntityFrameworkCore;
using Ulbra.One.Common.Application.Ai;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Finance.Application.Endpoints.Payables.AiClassificationSuggestion;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Classification;

public sealed class PayableClassifier(IAiCompletion ai, IUnitOfWork uow) : IPayableClassifier
{
    private const double SuggestThreshold = 0.5;

    public async Task<PayableClassificationResult> ClassifyAsync(Payable payable, CancellationToken ct)
    {
        // Analytical accounting accounts (chart of accounts is global reference data).
        var accounts = await uow.ReadSet<ChartAccount>()
            .Where(c => c.Kind == AccountKind.Analytical && c.IsActive && c.DeletedAt == null
                        && c.ChartType == ChartType.Accounting)
            .ToListAsync(ct);

        var fiscalDoc = await uow.ReadSet<FiscalDocument>()
            .FirstOrDefaultAsync(d => d.PayableId == payable.Id, ct);

        var prompt = AiClassificationPrompt.Build(
            payable.Description, payable.CounterpartyName, payable.DocumentNumber, payable.TotalAmount,
            accounts.Select(c => (c.Code, c.Name)).ToList(),
            cfops: fiscalDoc?.Cfops,
            ncms: fiscalDoc?.Ncms);

        var aiResult = await ai.CompleteAsync<PayableClassificationSuggestion>(prompt, ct: ct);
        if (aiResult.IsSuccess && aiResult.Value.Confidence >= SuggestThreshold)
        {
            var catalog = accounts.Select(c => new AiChartAccountRef(c.Id, c.Code)).ToList();
            var acc = AiClassificationResolver.Resolve(aiResult.Value, catalog);
            if (acc is not null)
                return new PayableClassificationResult(acc, aiResult.Value.Confidence, aiResult.Value.Rationale, "ai");
        }

        var (hAcc, _) = await VendorHistorySuggestion.FindAsync(uow, payable, ct);
        if (hAcc is null)
            return new PayableClassificationResult(null, 0, "No suggestion available.", "none");

        return new PayableClassificationResult(hAcc.Value.Value, 0, "Based on prior classifications for this vendor.", "heuristic");
    }
}
