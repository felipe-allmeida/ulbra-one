using Ulbra.One.Finance.Domain;
using Ulbra.One.Fiscal.Domain;
using Ulbra.One.Fiscal.Domain.Assessment;
using Xunit;

namespace Ulbra.One.UnitTests.Fiscal;

public class WithholdingAssessmentCalculatorTests
{
    private static readonly Guid CompanyA = Guid.NewGuid();
    private static readonly Guid CcMkt = Guid.NewGuid();
    private static readonly DateOnly D = new(2026, 6, 15);

    private static WithholdingFact Fact(Guid payableId, WithholdingTaxType tax, decimal baseAmount, decimal amount,
        Guid? companyId = null, Guid? costCenterId = null) =>
        new(payableId, "Fornecedor X", "NF-1", companyId ?? CompanyA, "Empresa A", "11222333000181",
            costCenterId ?? CcMkt, "MKT", "Marketing", Guid.NewGuid(), "99888777000166",
            tax, baseAmount, amount, baseAmount, D);

    [Fact]
    public void Empty_input_yields_empty_assessment()
    {
        var a = WithholdingAssessmentCalculator.Build([]);

        Assert.Empty(a.ByCompany);
        Assert.Empty(a.ByRetentionGroup);
        Assert.Empty(a.Facts);
        Assert.Equal(0m, a.Totals.WithheldTotal);
        Assert.Equal(0, a.Totals.PayableCount);
    }

    [Fact]
    public void Groups_pis_cofins_csll_into_csrf_guia()
    {
        var p = Guid.NewGuid();

        var a = WithholdingAssessmentCalculator.Build(
        [
            Fact(p, WithholdingTaxType.Pis, 1000m, 6.50m),
            Fact(p, WithholdingTaxType.Cofins, 1000m, 30m),
            Fact(p, WithholdingTaxType.Csll, 1000m, 10m),
        ]);

        var csrf = Assert.Single(a.ByRetentionGroup, r => r.Group == RetentionGroup.Csrf);
        Assert.Equal(46.50m, csrf.WithheldAmount);
        Assert.Equal(1, csrf.PayableCount);
    }

    [Fact]
    public void Aggregates_by_company_with_distinct_payable_count()
    {
        var p1 = Guid.NewGuid();
        var p2 = Guid.NewGuid();

        var a = WithholdingAssessmentCalculator.Build(
        [
            Fact(p1, WithholdingTaxType.Irrf, 1000m, 15m),
            Fact(p1, WithholdingTaxType.Inss, 1000m, 110m),
            Fact(p2, WithholdingTaxType.Irrf, 2000m, 30m),
        ]);

        var company = Assert.Single(a.ByCompany);
        Assert.Equal(155m, company.WithheldAmount);
        Assert.Equal(2, company.PayableCount);   // p1 + p2 distinct
        Assert.Equal(2, a.Totals.PayableCount);
        Assert.Equal(155m, a.Totals.WithheldTotal);
    }

    [Fact]
    public void Iss_is_kept_in_the_assessment() // painel é agnóstico; só o XML da EFD-Reinf descarta ISS
    {
        var a = WithholdingAssessmentCalculator.Build([Fact(Guid.NewGuid(), WithholdingTaxType.Iss, 1000m, 50m)]);

        Assert.Contains(a.ByRetentionGroup, r => r.Group == RetentionGroup.Iss);
        Assert.Equal(50m, a.Totals.WithheldTotal);
    }

    [Fact]
    public void By_tax_maps_each_tax_to_its_group()
    {
        var a = WithholdingAssessmentCalculator.Build([Fact(Guid.NewGuid(), WithholdingTaxType.Cofins, 1000m, 30m)]);

        var row = Assert.Single(a.ByTaxType);
        Assert.Equal(WithholdingTaxType.Cofins, row.TaxType);
        Assert.Equal(RetentionGroup.Csrf, row.Group);
    }

    [Fact]
    public void By_cost_center_sums_withheld_across_payables()
    {
        var cc = Guid.NewGuid();
        var a = WithholdingAssessmentCalculator.Build(
        [
            Fact(Guid.NewGuid(), WithholdingTaxType.Irrf, 1000m, 15m, costCenterId: cc),
            Fact(Guid.NewGuid(), WithholdingTaxType.Irrf, 1000m, 15m, costCenterId: cc),
        ]);

        var row = Assert.Single(a.ByCostCenter, r => r.CostCenterId == cc);
        Assert.Equal(30m, row.WithheldAmount);
        Assert.Equal(2, row.PayableCount);
    }
}

public class AssessmentPeriodTests
{
    [Fact]
    public void PerApur_formats_year_month_zero_padded()
    {
        Assert.Equal("2026-06", AssessmentPeriod.From(new DateOnly(2026, 6, 15)).PerApur);
        Assert.Equal("2026-11", new AssessmentPeriod(2026, 11).PerApur);
    }
}
