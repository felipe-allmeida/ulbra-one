using Ulbra.One.Common.Application.Ids;

namespace Ulbra.One.Finance.Application.Contracts;

/// <summary>An account-classification suggestion. Source is "ai" or "heuristic". Does not mutate the payable.</summary>
public sealed record AiClassificationSuggestionResponse(
    ChartAccountId? AccountingAccountId,
    ChartAccountId? FinancialAccountId,
    double Confidence,
    string Rationale,
    string Source);
