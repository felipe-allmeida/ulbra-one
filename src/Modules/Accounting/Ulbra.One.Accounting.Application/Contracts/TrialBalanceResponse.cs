using Ulbra.One.Accounting.Application.Reports;
using Ulbra.One.Accounting.Domain;
using Ulbra.One.Common.Application.Ids;

namespace Ulbra.One.Accounting.Application.Contracts;

/// <summary>One account's row in the trial balance (balancete).</summary>
public sealed record TrialBalanceRowResponse(
    LedgerAccountId LedgerAccountId,
    string AccountCode,
    string AccountName,
    AccountGroup AccountGroup,
    NormalBalance NormalBalance,
    decimal TotalDebit,
    decimal TotalCredit,
    decimal Balance)
{
    public static TrialBalanceRowResponse From(TrialBalanceRow r) => new(
        new LedgerAccountId(r.LedgerAccountId), r.AccountCode, r.AccountName,
        r.AccountGroup, r.NormalBalance, r.TotalDebit, r.TotalCredit, r.Balance);
}

/// <summary>The trial balance (balancete) for a company (or consolidated when <paramref name="CompanyId"/> is null).</summary>
public sealed record TrialBalanceResponse(
    CompanyId? CompanyId,
    IReadOnlyList<TrialBalanceRowResponse> Rows,
    decimal TotalDebit,
    decimal TotalCredit,
    bool IsBalanced)
{
    public static TrialBalanceResponse From(TrialBalance tb, CompanyId? companyId) => new(
        companyId,
        tb.Rows.Select(TrialBalanceRowResponse.From).ToList(),
        tb.TotalDebit,
        tb.TotalCredit,
        tb.IsBalanced);
}
