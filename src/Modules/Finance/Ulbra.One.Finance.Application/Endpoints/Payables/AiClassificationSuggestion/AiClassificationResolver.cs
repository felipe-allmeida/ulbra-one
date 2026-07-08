namespace Ulbra.One.Finance.Application.Endpoints.Payables.AiClassificationSuggestion;

/// <summary>Maps the LLM's chosen code to a chart-account id, guarding against hallucinated codes.
/// The catalog contains only analytical, payable-receivable accounts.</summary>
public static class AiClassificationResolver
{
    public static Guid? Resolve(
        PayableClassificationSuggestion suggestion, IReadOnlyList<AiChartAccountRef> catalog)
    {
        if (string.IsNullOrWhiteSpace(suggestion.AccountingAccountCode)) return null;
        var match = catalog.FirstOrDefault(c => c.Code == suggestion.AccountingAccountCode);
        return match?.Id;
    }
}
