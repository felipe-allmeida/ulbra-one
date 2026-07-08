using Ulbra.One.Finance.Domain;

namespace Ulbra.One.Fiscal.Domain.Assessment;

/// <summary>One withholding line within a competência, flattened with the display fields the assessment groups
/// by. Pure input for <see cref="WithholdingAssessmentCalculator"/> — no EF types. Loaded from the payable +
/// its company / cost center / vendor by the Application-layer loader.</summary>
public sealed record WithholdingFact(
    Guid PayableId,
    string CounterpartyName,
    string? DocumentNumber,
    Guid CompanyId,
    string CompanyName,
    string CompanyCnpj,
    Guid CostCenterId,
    string CostCenterCode,
    string CostCenterName,
    Guid VendorId,
    string VendorDocument,
    WithholdingTaxType TaxType,
    decimal BaseAmount,
    decimal Amount,
    // Gross amount of the whole payable (its TotalAmount) — repeated on every line. The EFD-Reinf R-4020 export
    // needs it for vlrBruto (payment gross), which is not the per-tax retention base. Ignored by the assessment.
    decimal GrossAmount,
    DateOnly IssueDate,
    string? Lc116Subitem = null)
{
    /// <summary>Collection group (guia de recolhimento) for this tax — the assessment's fiscal grouping axis
    /// (IRRF / CSRF / INSS / ISS). PIS+COFINS+CSLL fold into CSRF.</summary>
    public RetentionGroup Group => RetentionGroups.For(TaxType);
}
