using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Accounting.Application.Reports;
using Ulbra.One.Accounting.Domain;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;

namespace Ulbra.One.Accounting.Application.Contracts;

/// <summary>An account line in the DRE. <see cref="Amount"/> is the account's contribution to the result
/// (credit − debit): positive for revenues, negative for costs/expenses/deductions.</summary>
[SwaggerSchema("One account's contribution to the result.")]
public sealed record IncomeStatementLineResponse(
    LedgerAccountId LedgerAccountId,
    [property: SwaggerSchema("Hierarchical account code.")]
    [property: SwaggerExample("3.1.2.04")]
    string AccountCode,
    [property: SwaggerSchema("Account name.")]
    [property: SwaggerExample("Manutenção e conservação predial")]
    string AccountName,
    [property: SwaggerSchema("Which line of the DRE cascade this account rolls into.")]
    DreLine DreLine,
    [property: SwaggerSchema("Contribution to the result (credit minus debit), in BRL. Revenues come out "
        + "positive and costs, expenses and deductions negative — so the cascade adds these rather than "
        + "subtracting, and a positive expense line means something was posted backwards.")]
    [property: SwaggerExample("-4500.00")]
    decimal Amount)
{
    public static IncomeStatementLineResponse From(IncomeStatementLine l) => new(
        new LedgerAccountId(l.LedgerAccountId), l.AccountCode, l.AccountName, l.DreLine, l.Amount);
}

/// <summary>One line of the DRE cascade: the accounts of a <see cref="DreLine"/> and their combined total.</summary>
[SwaggerSchema("One step of the DRE cascade: the accounts that make it up and their combined total.")]
public sealed record IncomeStatementSectionResponse(
    [property: SwaggerSchema("Which step of the cascade this section is.")]
    DreLine Line,
    [property: SwaggerSchema("Sum of the section's lines, in BRL, with the same sign convention.")]
    [property: SwaggerExample("-182400.00")]
    decimal Total,
    [property: SwaggerSchema("The accounts in this section.")]
    IReadOnlyList<IncomeStatementLineResponse> Lines)
{
    public static IncomeStatementSectionResponse From(IncomeStatementSection s) => new(
        s.Line, s.Total, s.Lines.Select(IncomeStatementLineResponse.From).ToList());
}

/// <summary>The structured DRE for a competence range, consolidated or per-company, optionally filtered by cost
/// center. Sections are ordered down the cascade; the subtotals are running sums (positive = lucro).</summary>
[SwaggerSchema("The DRE for a competence range. Every subtotal is a running sum down the cascade, so "
    + "they are checkpoints along one calculation and must not be added together.")]
public sealed record IncomeStatementResponse(
    [property: SwaggerSchema("Company the report covers. Null means consolidated.")]
    CompanyId? CompanyId,
    [property: SwaggerSchema("Cost center the report was filtered to. Null means unfiltered.")]
    CostCenterId? CostCenterId,
    [property: SwaggerSchema("Start of the competence range.")]
    [property: SwaggerExample("2026-01-01")]
    DateOnly From,
    [property: SwaggerSchema("End of the competence range, inclusive.")]
    [property: SwaggerExample("2026-07-31")]
    DateOnly To,
    [property: SwaggerSchema("The cascade, in order. The subtotals below are running sums of these.")]
    IReadOnlyList<IncomeStatementSectionResponse> Sections,
    [property: SwaggerSchema("Receita bruta, in BRL.")]
    [property: SwaggerExample("2400000.00")]
    decimal GrossRevenue,
    [property: SwaggerSchema("Deductions from revenue — taxes on sales, returns, discounts. Negative, like every other subtraction in the cascade.")]
    [property: SwaggerExample("-288000.00")]
    decimal RevenueDeductions,
    [property: SwaggerSchema("Receita líquida: gross revenue plus the deductions (which are negative).")]
    [property: SwaggerExample("2112000.00")]
    decimal NetRevenue,
    [property: SwaggerSchema("Cost of goods or services sold. Negative.")]
    [property: SwaggerExample("-1180000.00")]
    decimal CostOfSales,
    [property: SwaggerSchema("Lucro bruto: net revenue plus cost of sales.")]
    [property: SwaggerExample("932000.00")]
    decimal GrossProfit,
    [property: SwaggerSchema("Other operating income, outside the main revenue line.")]
    [property: SwaggerExample("18000.00")]
    decimal OtherOperatingIncome,
    [property: SwaggerSchema("Operating expenses — administrative, commercial, general. Negative.")]
    [property: SwaggerExample("-690000.00")]
    decimal OperatingExpenses,
    [property: SwaggerSchema("Resultado operacional: gross profit plus other operating income and expenses.")]
    [property: SwaggerExample("260000.00")]
    decimal OperatingResult,
    [property: SwaggerSchema("Financial income — interest earned, discounts obtained.")]
    [property: SwaggerExample("12000.00")]
    decimal FinancialRevenue,
    [property: SwaggerSchema("Financial expense — interest paid, bank fees, fines. Negative.")]
    [property: SwaggerExample("-31000.00")]
    decimal FinancialExpense,
    [property: SwaggerSchema("Resultado financeiro: financial income plus financial expense.")]
    [property: SwaggerExample("-19000.00")]
    decimal FinancialResult,
    [property: SwaggerSchema("Non-operating result that fits none of the sections above.")]
    [property: SwaggerExample("0.00")]
    decimal OtherResult,
    [property: SwaggerSchema("Resultado antes dos tributos: operating result plus financial and other results.")]
    [property: SwaggerExample("241000.00")]
    decimal PreTaxResult,
    [property: SwaggerSchema("IRPJ on the result. Negative.")]
    [property: SwaggerExample("-36150.00")]
    decimal IncomeTax,
    [property: SwaggerSchema("CSLL on the result. Negative.")]
    [property: SwaggerExample("-21690.00")]
    decimal SocialContribution,
    [property: SwaggerSchema("Resultado líquido do período: the bottom line, positive for profit. It is what the balance sheet carries into equity as the period result.")]
    [property: SwaggerExample("183160.00")]
    decimal NetIncome)
{
    public static IncomeStatementResponse Of(IncomeStatement dre, CompanyId? companyId, CostCenterId? costCenterId) => new(
        companyId,
        costCenterId,
        dre.From,
        dre.To,
        dre.Sections.Select(IncomeStatementSectionResponse.From).ToList(),
        dre.GrossRevenue,
        dre.RevenueDeductions,
        dre.NetRevenue,
        dre.CostOfSales,
        dre.GrossProfit,
        dre.OtherOperatingIncome,
        dre.OperatingExpenses,
        dre.OperatingResult,
        dre.FinancialRevenue,
        dre.FinancialExpense,
        dre.FinancialResult,
        dre.OtherResult,
        dre.PreTaxResult,
        dre.IncomeTax,
        dre.SocialContribution,
        dre.NetIncome);
}
