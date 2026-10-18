using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;

namespace Ulbra.One.Finance.Application.Contracts;

/// <summary>An account-classification suggestion. Source is "ai" or "heuristic". Does not mutate the payable.</summary>
[SwaggerSchema("An account-classification suggestion. It does NOT change the payable — applying it is "
    + "a separate, explicit step by the accountant.")]
public sealed record AiClassificationSuggestionResponse(
    [property: SwaggerSchema("The suggested account. Null when nothing could be suggested.")]
    LedgerAccountId? AccountingAccountId,
    [property: SwaggerSchema("How confident the suggestion is, 0 to 1.")]
    [property: SwaggerExample("0.92")]
    double Confidence,
    [property: SwaggerSchema("Why this account was suggested, in plain language — so the accountant can "
        + "judge it rather than take it on trust.")]
    [property: SwaggerExample("Fornecedor classificado em 3.1.2.04 nas últimas 6 notas de manutenção predial.")]
    string Rationale,
    [property: SwaggerSchema("Where the suggestion came from: `ai` or `heuristic`. The heuristic one is "
        + "history-based and deterministic; the AI one reads the document.")]
    [property: SwaggerExample("ai")]
    string Source);
