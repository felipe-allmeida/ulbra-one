using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Finance.Domain;
using Ulbra.One.Fiscal.Domain;

namespace Ulbra.One.Fiscal.Application.Endpoints.Assessment;

/// <summary>Query-string filters for the withholding assessment. Bound via <c>[AsParameters]</c>.</summary>
public sealed record FiscalAssessmentRequest
{
    [SwaggerSchema("Assessment period (competência) in `YYYY-MM`. Required; anything else returns 400.")]
    [SwaggerExample("2026-08")]
    public string PerApur { get; set; } = string.Empty;

    [SwaggerSchema("Restrict to one company (contribuinte). Omit to assess across every company.")]
    public CompanyId? CompanyId { get; set; }

    [SwaggerSchema("Restrict to one cost center. Omit to include every cost center.")]
    public CostCenterId? CostCenterId { get; set; }

    [SwaggerSchema("Restrict to a single tax. Omit to include every withholding tax.")]
    public WithholdingTaxType? TaxType { get; set; }

    [SwaggerSchema("Restrict to one retention group — the grouping that maps to a payment guia.")]
    public RetentionGroup? RetentionGroup { get; set; }
}
