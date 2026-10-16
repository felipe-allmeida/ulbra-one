using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Budgeting.Application.BudgetControl;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;

namespace Ulbra.One.Budgeting.Application.Contracts;

/// <summary>One "orçamento por natureza" row: a cost center's ceiling (orçado) and its consumption
/// (realizado + empenhado) plus previsto and available, for a single competency.</summary>
[SwaggerSchema("One cost center's budget position for a single competency. The figures overlap by "
    + "design — Consumed is Realized plus Committed, so adding all of them double-counts.")]
public sealed record CostCenterBudgetLineResponse(
    CostCenterId CostCenterId,
    [property: SwaggerSchema("Cost center name, denormalized so the row renders without a second call.")]
    [property: SwaggerExample("Manutenção predial")]
    string CostCenterName,
    [property: SwaggerSchema("The month's ceiling, in BRL. Zero when no budget cell exists — see "
        + "Budgeted, which distinguishes that from a ceiling deliberately set to zero.")]
    [property: SwaggerExample("40000.00")]
    decimal Orcado,
    [property: SwaggerSchema("Paid in the month, in BRL.")]
    [property: SwaggerExample("18200.00")]
    decimal Realized,
    [property: SwaggerSchema("Empenhado, in BRL — approved but not yet paid.")]
    [property: SwaggerExample("9400.00")]
    decimal Committed,
    [property: SwaggerSchema("Previsto, in BRL — what is in the pipeline but not yet approved, so it "
        + "does not consume the ceiling yet.")]
    [property: SwaggerExample("5000.00")]
    decimal Forecast,
    [property: SwaggerSchema("What actually consumes the ceiling, in BRL: Realized plus Committed.")]
    [property: SwaggerExample("27600.00")]
    decimal Consumed,
    [property: SwaggerSchema("Ceiling minus Consumed, in BRL. This is what a new requisition is checked "
        + "against — Forecast is not subtracted.")]
    [property: SwaggerExample("12400.00")]
    decimal Available,
    [property: SwaggerSchema("Whether a budget cell exists for this month. False means unbudgeted, which "
        + "is not the same as a ceiling of zero — it is why an Orcado of 0 needs this flag to be read.")]
    bool Budgeted)
{
    public static CostCenterBudgetLineResponse From(CostCenterBudget b) =>
        new(new CostCenterId(b.CostCenterId), b.CostCenterName, b.Orcado, b.Realized, b.Committed, b.Forecast,
            b.Consumed, b.Available, b.Budgeted);
}

/// <summary>Total realized (spend) of a cost center over all competencies — cost-center list card.</summary>
[SwaggerSchema("A cost center's total spend across every competency — the figure on the cost-center "
    + "list card. Not scoped to a month, unlike the budget line.")]
public sealed record CostCenterRealizedResponse(
    CostCenterId CostCenterId,
    [property: SwaggerSchema("Total paid against this cost center, in BRL, over all time.")]
    [property: SwaggerExample("284500.00")]
    decimal Realized)
{
    public static CostCenterRealizedResponse From(CostCenterRealized r) =>
        new(new CostCenterId(r.CostCenterId), r.Realized);
}
