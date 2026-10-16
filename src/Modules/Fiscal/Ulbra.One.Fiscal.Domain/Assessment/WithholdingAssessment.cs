using Ulbra.One.Finance.Domain;

namespace Ulbra.One.Fiscal.Domain.Assessment;

/// <summary>Headline totals across the withholdings in the competência.</summary>
public sealed record AssessmentTotals(decimal BaseTotal, decimal WithheldTotal, int PayableCount);

/// <summary>Withheld per company/establishment (fiscal dimension — becomes the ideEstab on export).</summary>
public sealed record CompanyAssessmentRow(
    Guid CompanyId, string CompanyName, string Cnpj, decimal BaseAmount, decimal WithheldAmount, int PayableCount);

/// <summary>Withheld per guia de recolhimento (IRRF / CSRF / INSS / ISS).</summary>
public sealed record RetentionGroupRow(
    RetentionGroup Group, decimal BaseAmount, decimal WithheldAmount, int PayableCount);

/// <summary>Withheld per atomic tribute, with its guia.</summary>
public sealed record TaxTypeRow(
    WithholdingTaxType TaxType, RetentionGroup Group, decimal BaseAmount, decimal WithheldAmount, int PayableCount);

/// <summary>Withheld per cost center — internal management view (NOT a fiscal dimension; never goes to the XML).</summary>
public sealed record CostCenterAssessmentRow(
    Guid CostCenterId, string Code, string Name, decimal WithheldAmount, int PayableCount);

/// <summary>The consolidated withholding assessment (apuração), format-agnostic. Fiscal grouping is by
/// company/establishment and retention group; by-cost-center is internal (management) analytics only. This type
/// never carries EFD-Reinf event codes — those are an export concern (see the EfdReinf adapter).</summary>
public sealed record WithholdingAssessment(
    AssessmentTotals Totals,
    IReadOnlyList<CompanyAssessmentRow> ByCompany,
    IReadOnlyList<RetentionGroupRow> ByRetentionGroup,
    IReadOnlyList<TaxTypeRow> ByTaxType,
    IReadOnlyList<CostCenterAssessmentRow> ByCostCenter,
    IReadOnlyList<WithholdingFact> Facts);

/// <summary>Pure calculator that folds withholding facts into a consolidated assessment. No EF, no I/O.
/// Never emits EFD-Reinf event codes — those live in the EfdReinf export adapter.</summary>
public static class WithholdingAssessmentCalculator
{
    public static WithholdingAssessment Build(IEnumerable<WithholdingFact> facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var list = facts as IReadOnlyList<WithholdingFact> ?? facts.ToList();

        var byCompany = list
            .GroupBy(f => f.CompanyId)
            .Select(g => new CompanyAssessmentRow(
                g.Key, g.First().CompanyName, g.First().CompanyCnpj,
                g.Sum(f => f.BaseAmount), g.Sum(f => f.Amount),
                g.Select(f => f.PayableId).Distinct().Count()))
            .OrderByDescending(r => r.WithheldAmount)
            .ToList();

        var byRetentionGroup = list
            .GroupBy(f => f.Group)
            .Select(g => new RetentionGroupRow(
                g.Key, g.Sum(f => f.BaseAmount), g.Sum(f => f.Amount),
                g.Select(f => f.PayableId).Distinct().Count()))
            .OrderBy(r => r.Group)
            .ToList();

        var byTaxType = list
            .GroupBy(f => f.TaxType)
            .Select(g => new TaxTypeRow(
                g.Key, RetentionGroups.For(g.Key), g.Sum(f => f.BaseAmount), g.Sum(f => f.Amount),
                g.Select(f => f.PayableId).Distinct().Count()))
            .OrderBy(r => r.TaxType)
            .ToList();

        var byCostCenter = list
            .GroupBy(f => f.CostCenterId)
            .Select(g => new CostCenterAssessmentRow(
                g.Key, g.First().CostCenterCode, g.First().CostCenterName,
                g.Sum(f => f.Amount), g.Select(f => f.PayableId).Distinct().Count()))
            .OrderByDescending(r => r.WithheldAmount)
            .ToList();

        var totals = new AssessmentTotals(
            list.Sum(f => f.BaseAmount),
            list.Sum(f => f.Amount),
            list.Select(f => f.PayableId).Distinct().Count());

        return new WithholdingAssessment(totals, byCompany, byRetentionGroup, byTaxType, byCostCenter, list);
    }
}
