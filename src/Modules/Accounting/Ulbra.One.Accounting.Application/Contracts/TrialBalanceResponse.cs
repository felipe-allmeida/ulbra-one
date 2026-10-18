using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Accounting.Application.Reports;
using Ulbra.One.Accounting.Domain;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;

namespace Ulbra.One.Accounting.Application.Contracts;

/// <summary>One account's row in the trial balance (balancete): opening balance (saldo anterior), the period's
/// debit/credit movement and the closing balance (saldo final = <see cref="Balance"/>). Opening and closing are
/// signed by the account's normal balance.</summary>
[SwaggerSchema("One account's row in the balancete: where it started, what moved in the period, and "
    + "where it ended. Opening and closing are signed by the account's normal balance, so a positive "
    + "number means the account is on its expected side.")]
public sealed record TrialBalanceRowResponse(
    LedgerAccountId LedgerAccountId,
    [property: SwaggerSchema("Hierarchical account code.")]
    [property: SwaggerExample("3.1.2.04")]
    string AccountCode,
    [property: SwaggerSchema("Short code used by the accountants, when the account has one.")]
    [property: SwaggerExample("41204")]
    string? ReducedCode,
    [property: SwaggerSchema("Account name.")]
    [property: SwaggerExample("Manutenção e conservação predial")]
    string AccountName,
    [property: SwaggerSchema("Which side of the statements the account belongs to.")]
    AccountGroup AccountGroup,
    [property: SwaggerSchema("The side that increases this account — it is what gives OpeningBalance and "
        + "Balance their sign.")]
    NormalBalance NormalBalance,
    [property: SwaggerSchema("Saldo anterior: the balance carried in from before the range, in BRL. Zero "
        + "when the range covers the account's whole history.")]
    [property: SwaggerExample("12000.00")]
    decimal OpeningBalance,
    [property: SwaggerSchema("Debits posted within the range, in BRL. Movement only — it excludes the "
        + "opening balance.")]
    [property: SwaggerExample("4500.00")]
    decimal TotalDebit,
    [property: SwaggerSchema("Credits posted within the range, in BRL.")]
    [property: SwaggerExample("0.00")]
    decimal TotalCredit,
    [property: SwaggerSchema("Saldo final, in BRL: the opening balance plus the period's movement, signed "
        + "by the normal balance.")]
    [property: SwaggerExample("16500.00")]
    decimal Balance)
{
    public static TrialBalanceRowResponse From(TrialBalanceRow r) => new(
        new LedgerAccountId(r.LedgerAccountId), r.AccountCode, r.ReducedCode, r.AccountName,
        r.AccountGroup, r.NormalBalance, r.OpeningBalance, r.TotalDebit, r.TotalCredit, r.Balance);
}

/// <summary>The trial balance (balancete) for a company (or consolidated when <paramref name="CompanyId"/> is
/// null). Echoes the requested competence range and cost-center filter (all null = full-history, unfiltered).</summary>
[SwaggerSchema("The trial balance (balancete). Echoes the filters it was built with, so a stored or "
    + "shared result still says what it covers.")]
public sealed record TrialBalanceResponse(
    [property: SwaggerSchema("Company the report covers. Null means consolidated across every company the "
        + "caller may see.")]
    CompanyId? CompanyId,
    [property: SwaggerSchema("Start of the competence range. Null means from the beginning of history, "
        + "which is also when every opening balance is zero.")]
    [property: SwaggerExample("2026-07-01")]
    DateOnly? From,
    [property: SwaggerSchema("End of the competence range, inclusive. Null means up to the latest entry.")]
    [property: SwaggerExample("2026-07-31")]
    DateOnly? To,
    [property: SwaggerSchema("Cost center the report was filtered to. Null means unfiltered — and note a "
        + "filtered balancete will not balance, since cost centers only tag part of each entry.")]
    CostCenterId? CostCenterId,
    [property: SwaggerSchema("One row per account with movement or a balance in the range.")]
    IReadOnlyList<TrialBalanceRowResponse> Rows,
    [property: SwaggerSchema("Sum of every row's debits, in BRL.")]
    [property: SwaggerExample("1250000.00")]
    decimal TotalDebit,
    [property: SwaggerSchema("Sum of every row's credits, in BRL.")]
    [property: SwaggerExample("1250000.00")]
    decimal TotalCredit,
    [property: SwaggerSchema("Whether total debits equal total credits. False on an unfiltered report "
        + "means the ledger itself is inconsistent and should be investigated — but it is expected "
        + "whenever CostCenterId is set.")]
    bool IsBalanced)
{
    public static TrialBalanceResponse Of(
        TrialBalance tb, CompanyId? companyId, DateOnly? from, DateOnly? to, CostCenterId? costCenterId) => new(
        companyId,
        from,
        to,
        costCenterId,
        tb.Rows.Select(TrialBalanceRowResponse.From).ToList(),
        tb.TotalDebit,
        tb.TotalCredit,
        tb.IsBalanced);
}
