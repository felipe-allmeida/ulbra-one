using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.AiClassificationSuggestion;

/// <summary>Maps the LLM's chosen codes to chart-account ids, guarding against hallucinated/wrong-axis codes.
/// The catalog contains only analytical, payable-receivable accounts.</summary>
public static class AiClassificationResolver
{
    public static (Guid? AccountingAccountId, Guid? FinancialAccountId) Resolve(
        PayableClassificationSuggestion suggestion, IReadOnlyList<AiChartAccountRef> catalog)
    {
        var acc = Match(suggestion.AccountingAccountCode, ChartType.Accounting, catalog);
        var fin = Match(suggestion.FinancialAccountCode, ChartType.Financial, catalog);
        return (acc, fin);
    }

    private static Guid? Match(string? code, ChartType type, IReadOnlyList<AiChartAccountRef> catalog)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var match = catalog.FirstOrDefault(c => c.Type == type && c.Code == code);
        return match?.Id;
    }
}
