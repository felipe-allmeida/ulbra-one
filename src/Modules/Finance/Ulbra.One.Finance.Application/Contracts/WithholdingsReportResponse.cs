using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Finance.Domain;

namespace Ulbra.One.Finance.Application.Contracts;

/// <summary>Consolidated withholding (retenção) report: headline totals, a breakdown per atomic tax, and the
/// per-payable detail. PIS/COFINS/CSLL are grouped into CSRF on the client for display.</summary>
public sealed record WithholdingsReportResponse(
    WithholdingsTotals Totals,
    IReadOnlyList<WithholdingByTaxRow> ByTax,
    IReadOnlyList<WithholdingPayableRow> ByPayable);

/// <summary>Headline totals across the payables that carry withholdings.</summary>
public sealed record WithholdingsTotals(decimal GrossTotal, decimal WithheldTotal, decimal NetTotal, int PayableCount);

/// <summary>Total withheld per atomic tax (IRRF/PIS/COFINS/CSLL/INSS/ISS), with how many payables contributed.</summary>
public sealed record WithholdingByTaxRow(WithholdingTaxType TaxType, decimal BaseAmount, decimal Amount, int PayableCount);

/// <summary>A payable that carries withholdings, with its gross/withheld/net and the per-tax lines.</summary>
public sealed record WithholdingPayableRow(
    PayableId Id, VendorId VendorId, string CounterpartyName, string? DocumentNumber,
    PayableStatus Status, decimal TotalAmount, decimal TotalWithheld, decimal NetTotal,
    IReadOnlyList<PayableWithholdingResponse> Lines);
