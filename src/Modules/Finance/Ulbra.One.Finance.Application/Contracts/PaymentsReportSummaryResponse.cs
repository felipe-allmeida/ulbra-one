using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.OpenApi;

namespace Ulbra.One.Finance.Application.Contracts;

/// <summary>Aggregate of the Relação de Pagamentos over the WHOLE filtered set (not a page), the data the
/// "Copiar Resumo" button turns into a WhatsApp-style text: a headline count + total, then breakdowns by unit
/// and by vendor. Amounts are gross installment sums, in BRL.</summary>
[SwaggerSchema("Whole-set aggregate of the payments report, behind the \"Copiar Resumo\" button.")]
public sealed record PaymentsReportSummaryResponse(
    [property: SwaggerSchema("How many installment rows the filtered set has.")]
    [property: SwaggerExample("16")]
    int Count,
    [property: SwaggerSchema("Sum of every row's installment amount, in BRL.")]
    [property: SwaggerExample("102108.85")]
    decimal Total,
    [property: SwaggerSchema("Totals grouped by unit, ordered by amount descending.")]
    IReadOnlyList<PaymentsReportGroupTotal> ByUnit,
    [property: SwaggerSchema("Totals grouped by vendor, ordered by amount descending.")]
    IReadOnlyList<PaymentsReportGroupTotal> ByProvider);

/// <summary>One line of a report breakdown: a group name and its summed amount (BRL).</summary>
[SwaggerSchema("One breakdown line: a group name and its summed amount, in BRL.")]
public sealed record PaymentsReportGroupTotal(
    [property: SwaggerSchema("Group label — a unit name or a vendor name.")]
    [property: SwaggerExample("Ulbra S.A")]
    string Name,
    [property: SwaggerSchema("Summed amount for the group, in BRL.")]
    [property: SwaggerExample("32873.94")]
    decimal Total);
