using Ulbra.One.Finance.Domain;
using Ulbra.One.Fiscal.Domain;
using Ulbra.One.Fiscal.Domain.Assessment;

namespace Ulbra.One.Fiscal.Application.Assessment;

/// <summary>Filters for the withholding assessment. <see cref="Period"/> (competência) is required; the rest
/// narrow the result. <c>Group</c> and <c>TaxType</c> filter the retention lines; company/cost center scope
/// the payables.</summary>
public sealed record AssessmentFilter(
    AssessmentPeriod Period,
    Guid? CompanyId = null,
    Guid? CostCenterId = null,
    WithholdingTaxType? TaxType = null,
    RetentionGroup? Group = null);
