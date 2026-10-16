using Ulbra.One.Accounting.Application.Seeding;
using Ulbra.One.Accounting.Domain;
using Xunit;

namespace Ulbra.One.UnitTests.Accounting;

public class DreLineClassifierTests
{
    [Theory]
    // Revenue (root 3)
    [InlineData("3.2.05.10.020", DreLine.GrossRevenue)]
    [InlineData("3.2.10.05.020", DreLine.RevenueDeductions)]
    [InlineData("3.2.25.05.020", DreLine.FinancialRevenue)]
    [InlineData("3.2.15.05.020", DreLine.OtherOperatingIncome)]
    [InlineData("3.2.20.05.020", DreLine.Other)]
    [InlineData("3.2.80.05.020", DreLine.Other)]
    [InlineData("3", DreLine.OtherOperatingIncome)]        // default revenue
    // Cost (root 5)
    [InlineData("5.2.10.05.020", DreLine.CostOfSales)]
    [InlineData("5", DreLine.CostOfSales)]
    // Expense (root 4)
    [InlineData("4.2.30.05.020", DreLine.FinancialExpense)]
    [InlineData("4.2.35.05.020", DreLine.SocialContribution)] // PROVISAO PARA CSLL
    [InlineData("4.2.35.05.060", DreLine.SocialContribution)] // CSLL - LUCROS DIFERIDOS
    [InlineData("4.2.35.05.040", DreLine.IncomeTax)]          // PROVISAO PARA IRPJ
    [InlineData("4.2.35.05.080", DreLine.IncomeTax)]          // IRPJ - LUCROS DIFERIDOS
    [InlineData("4.2.35", DreLine.IncomeTax)]                 // tax subtree default
    [InlineData("4.2.25.05.020", DreLine.Other)]
    [InlineData("4.2.05.05.020", DreLine.OperatingExpenses)]
    [InlineData("4.2.10.05.020", DreLine.OperatingExpenses)]
    [InlineData("4", DreLine.OperatingExpenses)]             // default expense
    public void Classifies_result_accounts_by_code_prefix(string code, DreLine expected) =>
        Assert.Equal(expected, DreLineClassifier.Classify(code));

    [Theory]
    [InlineData("1.2.05.05.020")]  // Ativo
    [InlineData("2.2.05.15.080")]  // Passivo
    [InlineData("2.6.20.05.020")]  // Patrimônio Líquido
    [InlineData("")]
    [InlineData("   ")]
    public void Non_result_or_blank_codes_are_unclassified(string code) =>
        Assert.Null(DreLineClassifier.Classify(code));

    [Fact]
    public void Prefix_match_is_segment_aware()
    {
        // "3.2.050" must not be treated as under the "3.2.05" subtree.
        Assert.Equal(DreLine.OtherOperatingIncome, DreLineClassifier.Classify("3.2.050"));
    }
}
