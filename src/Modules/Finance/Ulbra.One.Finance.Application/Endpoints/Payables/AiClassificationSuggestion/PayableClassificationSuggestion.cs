namespace Ulbra.One.Finance.Application.Endpoints.Payables.AiClassificationSuggestion;

/// <summary>Structured output the LLM must return: chosen account code + confidence + rationale.</summary>
public sealed record PayableClassificationSuggestion(
    string? AccountingAccountCode,
    double Confidence,
    string Rationale);

/// <summary>Lightweight projection of an analytical ledger account for prompt + resolution.</summary>
public sealed record AiAccountRef(Guid Id, string Code);
