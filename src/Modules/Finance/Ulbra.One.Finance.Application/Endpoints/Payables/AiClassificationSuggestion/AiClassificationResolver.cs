namespace Ulbra.One.Finance.Application.Endpoints.Payables.AiClassificationSuggestion;

/// <summary>Maps the LLM's chosen code to a ledger-account id, guarding against hallucinated codes.
/// The catalog contains only analytical, postable accounts.</summary>
public static class AiClassificationResolver
{
    public static Guid? Resolve(
        PayableClassificationSuggestion suggestion, IReadOnlyList<AiAccountRef> catalog)
    {
        if (string.IsNullOrWhiteSpace(suggestion.AccountingAccountCode)) return null;
        var match = catalog.FirstOrDefault(c => c.Code == suggestion.AccountingAccountCode);
        return match?.Id;
    }
}
