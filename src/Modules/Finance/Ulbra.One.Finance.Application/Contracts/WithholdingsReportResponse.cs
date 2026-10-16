using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Finance.Domain;

namespace Ulbra.One.Finance.Application.Contracts;

/// <summary>Consolidated withholding (retenção) report: headline totals, a breakdown per atomic tax, and the
/// per-payable detail. PIS/COFINS/CSLL are grouped into CSRF on the client for display.
///
/// Each amount is split into what is <b>already retido</b> (<c>Realized</c>) and what is still <b>previsto</b>
/// (<c>Foreseen</c>): emission-time taxes (IRRF/INSS/ISS) are realized at the document's competence, while the
/// CSRF group is only retained at each payment (baixa), so its planned amount stays "foreseen" until paid. The
/// planned (document-level) amount always equals Realized + Foreseen.</summary>
[SwaggerSchema("The withholding report. Every amount splits into what is already retido (Realized) and "
    + "what is still previsto (Foreseen), because the two are recognized at different moments: IRRF, INSS "
    + "and ISS at the document, the CSRF group only at each payment. Planned always equals Realized plus "
    + "Foreseen.")]
public sealed record WithholdingsReportResponse(
    [property: SwaggerSchema("Headline totals.")]
    WithholdingsTotals Totals,
    [property: SwaggerSchema("Breakdown per individual tax. PIS, COFINS and CSLL are grouped into CSRF by "
        + "the client for display — the server keeps them apart.")]
    IReadOnlyList<WithholdingByTaxRow> ByTax,
    [property: SwaggerSchema("The payables behind the totals, each with its own lines.")]
    IReadOnlyList<WithholdingPayableRow> ByPayable);

/// <summary>Headline totals across the payables that carry withholdings. <c>WithheldTotal</c> is the planned
/// (document-level) total = <c>RealizedTotal</c> + <c>ForeseenTotal</c>.</summary>
[SwaggerSchema("Headline totals across the payables that carry withholdings.")]
public sealed record WithholdingsTotals(
    [property: SwaggerSchema("Sum of the document totals, in BRL — before withholdings.")]
    [property: SwaggerExample("1250000.00")]
    decimal GrossTotal,
    [property: SwaggerSchema("Planned withholding, in BRL. Equals RealizedTotal plus ForeseenTotal.")]
    [property: SwaggerExample("58125.00")]
    decimal WithheldTotal,
    [property: SwaggerSchema("What the vendors receive, in BRL: gross minus withheld.")]
    [property: SwaggerExample("1191875.00")]
    decimal NetTotal,
    [property: SwaggerSchema("How many payables carry withholdings.")]
    [property: SwaggerExample("284")]
    int PayableCount,
    [property: SwaggerSchema("Already withheld, in BRL — recognized at the document (IRRF, INSS, ISS) or "
        + "at a payment that has happened.")]
    [property: SwaggerExample("41200.00")]
    decimal RealizedTotal,
    [property: SwaggerSchema("Still to be withheld, in BRL — chiefly the CSRF group on installments not "
        + "yet paid.")]
    [property: SwaggerExample("16925.00")]
    decimal ForeseenTotal);

/// <summary>Per atomic tax (IRRF/PIS/COFINS/CSLL/INSS/ISS): planned <c>Amount</c> split into <c>Realized</c>
/// (retido) and <c>Foreseen</c> (a reter), with how many payables contributed.</summary>
[SwaggerSchema("One tax's slice of the report, with its realized/foreseen split.")]
public sealed record WithholdingByTaxRow(
    [property: SwaggerSchema("The tax this row totals.")]
    WithholdingTaxType TaxType,
    [property: SwaggerSchema("Sum of the calculation bases, in BRL.")]
    [property: SwaggerExample("620000.00")]
    decimal BaseAmount,
    [property: SwaggerSchema("Planned withholding for this tax, in BRL — Realized plus Foreseen.")]
    [property: SwaggerExample("4030.00")]
    decimal Amount,
    [property: SwaggerSchema("How many payables contributed.")]
    [property: SwaggerExample("142")]
    int PayableCount,
    [property: SwaggerSchema("Already withheld for this tax, in BRL.")]
    [property: SwaggerExample("2810.00")]
    decimal RealizedAmount,
    [property: SwaggerSchema("Still to be withheld for this tax, in BRL.")]
    [property: SwaggerExample("1220.00")]
    decimal ForeseenAmount);

/// <summary>A payable that carries withholdings, with its gross/withheld/net, the realized/foreseen split, and
/// the per-tax lines.</summary>
[SwaggerSchema("One payable that carries withholdings, with its per-tax lines.")]
public sealed record WithholdingPayableRow(
    PayableId Id,
    VendorId VendorId,
    [property: SwaggerSchema("Vendor name as recorded on the payable.")]
    [property: SwaggerExample("Construtora Horizonte Ltda")]
    string CounterpartyName,
    [property: SwaggerSchema("Invoice number of the payable.")]
    [property: SwaggerExample("00012345")]
    string? DocumentNumber,
    [property: SwaggerSchema("Where the payable stands — it is what explains how much of the withholding "
        + "is still only foreseen.")]
    PayableStatus Status,
    [property: SwaggerSchema("Document total, in BRL.")]
    [property: SwaggerExample("4500.00")]
    decimal TotalAmount,
    [property: SwaggerSchema("Planned withholding on this payable, in BRL.")]
    [property: SwaggerExample("209.25")]
    decimal TotalWithheld,
    [property: SwaggerSchema("What the vendor receives, in BRL.")]
    [property: SwaggerExample("4290.75")]
    decimal NetTotal,
    [property: SwaggerSchema("The per-tax lines behind this payable's withholding.")]
    IReadOnlyList<WithholdingReportLine> Lines,
    [property: SwaggerSchema("Already withheld on this payable, in BRL.")]
    [property: SwaggerExample("58.50")]
    decimal RealizedAmount,
    [property: SwaggerSchema("Still to be withheld on this payable, in BRL.")]
    [property: SwaggerExample("150.75")]
    decimal ForeseenAmount);

/// <summary>One withholding line in the report: the planned <c>Amount</c> for the tax, when it is recognized
/// (<c>Timing</c> = OnIssue/OnPayment), and how much is already <c>Realized</c> vs still <c>Foreseen</c>.</summary>
[SwaggerSchema("One withholding line of a payable, carrying WHEN the tax is recognized — which is what "
    + "decides whether its amount counts as realized or foreseen.")]
public sealed record WithholdingReportLine(
    [property: SwaggerSchema("The tax this line is.")]
    WithholdingTaxType TaxType,
    [property: SwaggerSchema("Amount the rate applies to, in BRL.")]
    [property: SwaggerExample("4500.00")]
    decimal BaseAmount,
    [property: SwaggerSchema("Rate applied, as a percentage. Null when the amount came from a fixed "
        + "value.")]
    [property: SwaggerExample("4.65")]
    decimal? Rate,
    [property: SwaggerSchema("Planned withholding for this line, in BRL.")]
    [property: SwaggerExample("209.25")]
    decimal Amount,
    [property: SwaggerSchema("When the tax is recognized: at the document (OnIssue) or at each payment "
        + "(OnPayment). OnIssue lines are realized immediately; OnPayment lines stay foreseen until the "
        + "installment is paid.")]
    WithholdingTiming Timing,
    [property: SwaggerSchema("Already withheld on this line, in BRL.")]
    [property: SwaggerExample("58.50")]
    decimal RealizedAmount,
    [property: SwaggerSchema("Still to be withheld on this line, in BRL.")]
    [property: SwaggerExample("150.75")]
    decimal ForeseenAmount);
