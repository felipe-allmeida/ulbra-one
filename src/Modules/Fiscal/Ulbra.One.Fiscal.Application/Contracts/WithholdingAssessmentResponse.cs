using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Fiscal.Domain;
using Ulbra.One.Fiscal.Domain.Assessment;

namespace Ulbra.One.Fiscal.Application.Contracts;

/// <summary>Headline totals of the assessment.</summary>
public sealed record AssessmentTotalsResponse(decimal BaseTotal, decimal WithheldTotal, int PayableCount);

/// <summary>Retido por empresa/estabelecimento (dimensão fiscal).</summary>
public sealed record CompanyAssessmentRowResponse(
    CompanyId CompanyId, string CompanyName, string Cnpj, decimal BaseAmount, decimal WithheldAmount, int PayableCount);

/// <summary>Retido por guia de recolhimento (IRRF / CSRF / INSS / ISS).</summary>
public sealed record RetentionGroupRowResponse(
    RetentionGroup Group, decimal BaseAmount, decimal WithheldAmount, int PayableCount);

/// <summary>Retido por tributo atômico, com sua guia.</summary>
public sealed record TaxTypeRowResponse(
    WithholdingTaxType TaxType, RetentionGroup Group, decimal BaseAmount, decimal WithheldAmount, int PayableCount);

/// <summary>Retido por centro de custo — visão gerencial interna (não é dimensão fiscal).</summary>
public sealed record CostCenterAssessmentRowResponse(
    CostCenterId CostCenterId, string Code, string Name, decimal WithheldAmount, int PayableCount);

/// <summary>A withholding line, for the drill-down drawer (source of the by-company / by-cost-center rows).</summary>
public sealed record WithholdingFactResponse(
    PayableId PayableId, string CounterpartyName, string? DocumentNumber,
    CompanyId CompanyId, CostCenterId CostCenterId, VendorId VendorId, string VendorDocument,
    WithholdingTaxType TaxType, RetentionGroup Group, decimal BaseAmount, decimal Amount)
{
    public static WithholdingFactResponse From(WithholdingFact f) => new(
        new PayableId(f.PayableId), f.CounterpartyName, f.DocumentNumber,
        new CompanyId(f.CompanyId), new CostCenterId(f.CostCenterId), new VendorId(f.VendorId), f.VendorDocument,
        f.TaxType, f.Group, f.BaseAmount, f.Amount);
}

/// <summary>Consolidated withholding assessment (apuração) for a competência. Fiscal axes: by company/estab and
/// by retention group; by-cost-center is internal (management) analytics only — never exported to the XML.</summary>
public sealed record WithholdingAssessmentResponse(
    string PerApur,
    CompanyId? CompanyId,
    AssessmentTotalsResponse Totals,
    IReadOnlyList<CompanyAssessmentRowResponse> ByCompany,
    IReadOnlyList<RetentionGroupRowResponse> ByRetentionGroup,
    IReadOnlyList<TaxTypeRowResponse> ByTaxType,
    IReadOnlyList<CostCenterAssessmentRowResponse> ByCostCenter,
    IReadOnlyList<WithholdingFactResponse> Facts)
{
    public static WithholdingAssessmentResponse From(string perApur, CompanyId? companyId, WithholdingAssessment a) => new(
        perApur, companyId,
        new AssessmentTotalsResponse(a.Totals.BaseTotal, a.Totals.WithheldTotal, a.Totals.PayableCount),
        a.ByCompany.Select(r => new CompanyAssessmentRowResponse(
            new CompanyId(r.CompanyId), r.CompanyName, r.Cnpj, r.BaseAmount, r.WithheldAmount, r.PayableCount)).ToList(),
        a.ByRetentionGroup.Select(r => new RetentionGroupRowResponse(
            r.Group, r.BaseAmount, r.WithheldAmount, r.PayableCount)).ToList(),
        a.ByTaxType.Select(r => new TaxTypeRowResponse(
            r.TaxType, r.Group, r.BaseAmount, r.WithheldAmount, r.PayableCount)).ToList(),
        a.ByCostCenter.Select(r => new CostCenterAssessmentRowResponse(
            new CostCenterId(r.CostCenterId), r.Code, r.Name, r.WithheldAmount, r.PayableCount)).ToList(),
        a.Facts.Select(WithholdingFactResponse.From).ToList());
}
