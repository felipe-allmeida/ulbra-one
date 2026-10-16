using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Fiscal.Domain;
using Ulbra.One.Fiscal.Domain.Assessment;

namespace Ulbra.One.Fiscal.Application.Contracts;

/// <summary>Headline totals of the assessment.</summary>
[SwaggerSchema("The assessment's headline numbers — what the accountant checks first, before opening any "
    + "breakdown.")]
public sealed record AssessmentTotalsResponse(
    [property: SwaggerSchema("Sum of the calculation bases, in BRL. Not the sum of the payables: the base "
        + "of a withholding can be the gross, the net or the service portion, depending on the rule.")]
    [property: SwaggerExample("1250000.00")]
    decimal BaseTotal,
    [property: SwaggerSchema("Total actually withheld, in BRL — what has to be collected.")]
    [property: SwaggerExample("58125.00")]
    decimal WithheldTotal,
    [property: SwaggerSchema("How many payables contributed. A payable with two withholdings still counts "
        + "once here, while the by-tax breakdown counts it under each.")]
    [property: SwaggerExample("284")]
    int PayableCount);

/// <summary>Withheld per company/establishment (fiscal dimension).</summary>
[SwaggerSchema("One company's slice of the assessment. This is a fiscal dimension — the CNPJ here is the "
    + "one that collects, so this breakdown is what the XML export is built from.")]
public sealed record CompanyAssessmentRowResponse(
    CompanyId CompanyId,
    [property: SwaggerSchema("Company legal name, denormalized so the row renders without a second call.")]
    [property: SwaggerExample("Universidade Luterana do Brasil")]
    string CompanyName,
    [property: SwaggerSchema("CNPJ of the collecting establishment, digits only.")]
    [property: SwaggerExample("88332580000165")]
    string Cnpj,
    [property: SwaggerSchema("Calculation base attributed to this company, in BRL.")]
    [property: SwaggerExample("820000.00")]
    decimal BaseAmount,
    [property: SwaggerSchema("Withheld under this company's CNPJ, in BRL.")]
    [property: SwaggerExample("38130.00")]
    decimal WithheldAmount,
    [property: SwaggerSchema("Payables counted in this row.")]
    [property: SwaggerExample("190")]
    int PayableCount);

/// <summary>Withheld per guia de recolhimento (IRRF / CSRF / INSS / ISS).</summary>
[SwaggerSchema("One guia de recolhimento's slice. The group, not the individual tax, is what gets paid: "
    + "CSRF is a single guia covering PIS, COFINS and CSLL together.")]
public sealed record RetentionGroupRowResponse(
    [property: SwaggerSchema("The guia this row totals.")]
    RetentionGroup Group,
    [property: SwaggerSchema("Calculation base for this guia, in BRL.")]
    [property: SwaggerExample("620000.00")]
    decimal BaseAmount,
    [property: SwaggerSchema("Amount to collect under this guia, in BRL.")]
    [property: SwaggerExample("28830.00")]
    decimal WithheldAmount,
    [property: SwaggerSchema("Payables counted in this row.")]
    [property: SwaggerExample("142")]
    int PayableCount);

/// <summary>Withheld per atomic tribute, with its guia.</summary>
[SwaggerSchema("One individual tribute's slice, carrying the guia it is collected under. Finer than the "
    + "by-group view: it is what shows PIS apart from COFINS inside the same CSRF guia.")]
public sealed record TaxTypeRowResponse(
    [property: SwaggerSchema("The tribute this row totals.")]
    WithholdingTaxType TaxType,
    [property: SwaggerSchema("Guia this tribute is collected under. Several tributes share one guia, "
        + "which is why the by-group totals are not a simple re-listing of these rows.")]
    RetentionGroup Group,
    [property: SwaggerSchema("Calculation base for this tribute, in BRL.")]
    [property: SwaggerExample("620000.00")]
    decimal BaseAmount,
    [property: SwaggerSchema("Withheld for this tribute, in BRL.")]
    [property: SwaggerExample("4030.00")]
    decimal WithheldAmount,
    [property: SwaggerSchema("Payables counted in this row.")]
    [property: SwaggerExample("142")]
    int PayableCount);

/// <summary>Withheld per cost center — internal management view (not a fiscal dimension).</summary>
[SwaggerSchema("One cost center's slice. Management analytics only — it is NOT a fiscal dimension and "
    + "never reaches the XML export, so do not reconcile it against what is collected.")]
public sealed record CostCenterAssessmentRowResponse(
    CostCenterId CostCenterId,
    [property: SwaggerSchema("Accounting code of the cost center.")]
    [property: SwaggerExample("3.1.2.04")]
    string Code,
    [property: SwaggerSchema("Cost center name, denormalized so the row renders without a second call.")]
    [property: SwaggerExample("Manutenção predial")]
    string Name,
    [property: SwaggerSchema("Withheld on payables charged to this cost center, in BRL. There is no base "
        + "here on purpose — the base is a fiscal figure and this view is not.")]
    [property: SwaggerExample("9200.00")]
    decimal WithheldAmount,
    [property: SwaggerSchema("Payables counted in this row.")]
    [property: SwaggerExample("31")]
    int PayableCount);

/// <summary>A withholding line, for the drill-down drawer (source of the by-company / by-cost-center rows).</summary>
[SwaggerSchema("A single withholding line — one tribute on one payable. Every breakdown above is an "
    + "aggregation of these, so this is where a total that looks wrong gets explained.")]
public sealed record WithholdingFactResponse(
    PayableId PayableId,
    [property: SwaggerSchema("Counterparty name as recorded on the payable, denormalized for the drawer.")]
    [property: SwaggerExample("Construtora Horizonte Ltda")]
    string CounterpartyName,
    [property: SwaggerSchema("Invoice number of the payable, when one was registered.")]
    [property: SwaggerExample("00012345")]
    string? DocumentNumber,
    CompanyId CompanyId,
    CostCenterId CostCenterId,
    VendorId VendorId,
    [property: SwaggerSchema("Vendor CNPJ or CPF, digits only — the counterparty the tax is withheld from.")]
    [property: SwaggerExample("12345678000195")]
    string VendorDocument,
    [property: SwaggerSchema("Which tribute this line is.")]
    WithholdingTaxType TaxType,
    [property: SwaggerSchema("Guia the tribute is collected under.")]
    RetentionGroup Group,
    [property: SwaggerSchema("Calculation base of this line, in BRL.")]
    [property: SwaggerExample("4500.00")]
    decimal BaseAmount,
    [property: SwaggerSchema("Amount withheld on this line, in BRL.")]
    [property: SwaggerExample("209.25")]
    decimal Amount)
{
    public static WithholdingFactResponse From(WithholdingFact f) => new(
        new PayableId(f.PayableId), f.CounterpartyName, f.DocumentNumber,
        new CompanyId(f.CompanyId), new CostCenterId(f.CostCenterId), new VendorId(f.VendorId), f.VendorDocument,
        f.TaxType, f.Group, f.BaseAmount, f.Amount);
}

/// <summary>Consolidated withholding assessment (apuração) for a competência. Fiscal axes: by company/estab and
/// by retention group; by-cost-center is internal (management) analytics only — never exported to the XML.</summary>
[SwaggerSchema("The withholding assessment (apuração) for one competência, sliced every way the screen "
    + "needs. The same money is counted in every breakdown — they are views of one set of facts, not "
    + "amounts to be added together.")]
public sealed record WithholdingAssessmentResponse(
    [property: SwaggerSchema("Competência the assessment covers (`YYYY-MM`).")]
    [property: SwaggerExample("2026-07")]
    string PerApur,
    [property: SwaggerSchema("Company the assessment was filtered to. Null means it spans every company "
        + "the caller may see, which is the consolidated view.")]
    CompanyId? CompanyId,
    [property: SwaggerSchema("Headline totals.")]
    AssessmentTotalsResponse Totals,
    [property: SwaggerSchema("Fiscal breakdown by collecting establishment. This is the axis the XML "
        + "export is built from.")]
    IReadOnlyList<CompanyAssessmentRowResponse> ByCompany,
    [property: SwaggerSchema("Fiscal breakdown by guia de recolhimento — what actually gets paid.")]
    IReadOnlyList<RetentionGroupRowResponse> ByRetentionGroup,
    [property: SwaggerSchema("Breakdown by individual tribute, finer than the guia view.")]
    IReadOnlyList<TaxTypeRowResponse> ByTaxType,
    [property: SwaggerSchema("Management breakdown by cost center. Internal only — never exported.")]
    IReadOnlyList<CostCenterAssessmentRowResponse> ByCostCenter,
    [property: SwaggerSchema("Every withholding line behind the totals, for the drill-down drawer.")]
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
