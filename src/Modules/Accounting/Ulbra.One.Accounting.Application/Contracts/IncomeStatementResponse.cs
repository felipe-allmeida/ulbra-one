using Ulbra.One.Accounting.Application.Reports;
using Ulbra.One.Accounting.Domain;
using Ulbra.One.Common.Application.Ids;

namespace Ulbra.One.Accounting.Application.Contracts;

/// <summary>An account line in the income statement (DRE).</summary>
public sealed record IncomeStatementLineResponse(
    LedgerAccountId LedgerAccountId,
    string AccountCode,
    string AccountName,
    AccountGroup AccountGroup,
    decimal Amount)
{
    public static IncomeStatementLineResponse From(IncomeStatementLine l) => new(
        new LedgerAccountId(l.LedgerAccountId), l.AccountCode, l.AccountName, l.AccountGroup, l.Amount);
}

/// <summary>The income statement (DRE) for a competence range, consolidated or per-company.</summary>
public sealed record IncomeStatementResponse(
    CompanyId? CompanyId,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<IncomeStatementLineResponse> Revenues,
    IReadOnlyList<IncomeStatementLineResponse> Expenses,
    decimal TotalRevenue,
    decimal TotalExpense,
    decimal Result)
{
    public static IncomeStatementResponse Of(IncomeStatement dre, CompanyId? companyId) => new(
        companyId,
        dre.From,
        dre.To,
        dre.Revenues.Select(IncomeStatementLineResponse.From).ToList(),
        dre.Expenses.Select(IncomeStatementLineResponse.From).ToList(),
        dre.TotalRevenue,
        dre.TotalExpense,
        dre.Result);
}
