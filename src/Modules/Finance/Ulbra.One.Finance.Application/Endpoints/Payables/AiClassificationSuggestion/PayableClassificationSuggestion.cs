using Ulbra.One.Finance.Domain;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Application.Endpoints.Payables.AiClassificationSuggestion;

/// <summary>Structured output the LLM must return: chosen account codes + confidence + rationale.</summary>
public sealed record PayableClassificationSuggestion(
    string? AccountingAccountCode,
    string? FinancialAccountCode,
    double Confidence,
    string Rationale);

/// <summary>Lightweight projection of an analytical chart account for prompt + resolution.</summary>
public sealed record AiChartAccountRef(Guid Id, string Code, ChartType Type);
