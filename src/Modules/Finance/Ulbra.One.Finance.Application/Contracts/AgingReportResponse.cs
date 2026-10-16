using Swashbuckle.AspNetCore.Annotations;
using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Common.Application.OpenApi;
using Ulbra.One.Finance.Domain;

namespace Ulbra.One.Finance.Application.Contracts;

[SwaggerSchema("The aging buckets, totalled. Buckets are by days past due and are mutually exclusive, "
    + "so they do sum to the grand total — unlike most of the other reports here.")]
public sealed record AgingBucketTotals(
    [property: SwaggerSchema("Outstanding not yet due, in BRL.")]
    [property: SwaggerExample("420000.00")]
    decimal NotYetDue,
    [property: SwaggerSchema("Outstanding 1 to 30 days overdue, in BRL.")]
    [property: SwaggerExample("86000.00")]
    decimal Overdue1To30,
    [property: SwaggerSchema("Outstanding 31 to 60 days overdue, in BRL.")]
    [property: SwaggerExample("24000.00")]
    decimal Overdue31To60,
    [property: SwaggerSchema("Outstanding 61 to 90 days overdue, in BRL.")]
    [property: SwaggerExample("9000.00")]
    decimal Overdue61To90,
    [property: SwaggerSchema("Outstanding more than 90 days overdue, in BRL.")]
    [property: SwaggerExample("13500.00")]
    decimal Overdue90Plus,
    [property: SwaggerSchema("Everything outstanding, in BRL — the sum of the buckets.")]
    [property: SwaggerExample("552500.00")]
    decimal GrandTotal);

[SwaggerSchema("One vendor's aging — how much we owe them and how late it is.")]
public sealed record AgingByVendorRow(
    VendorId VendorId,
    [property: SwaggerSchema("Vendor name, denormalized so the row renders without a second call.")]
    [property: SwaggerExample("Construtora Horizonte Ltda")]
    string Name,
    [property: SwaggerSchema("Owed to this vendor, not yet due, in BRL.")]
    [property: SwaggerExample("42000.00")]
    decimal NotYetDue,
    [property: SwaggerSchema("Owed to this vendor, 1 to 30 days overdue, in BRL.")]
    [property: SwaggerExample("8600.00")]
    decimal Overdue1To30,
    [property: SwaggerSchema("Owed to this vendor, 31 to 60 days overdue, in BRL.")]
    [property: SwaggerExample("0.00")]
    decimal Overdue31To60,
    [property: SwaggerSchema("Owed to this vendor, 61 to 90 days overdue, in BRL.")]
    [property: SwaggerExample("0.00")]
    decimal Overdue61To90,
    [property: SwaggerSchema("Owed to this vendor, more than 90 days overdue, in BRL.")]
    [property: SwaggerExample("0.00")]
    decimal Overdue90Plus,
    [property: SwaggerSchema("Everything outstanding to this vendor, in BRL.")]
    [property: SwaggerExample("50600.00")]
    decimal Total);

[SwaggerSchema("One cost center's aging — the same buckets, sliced by who the spend is charged to.")]
public sealed record AgingByCostCenterRow(
    CostCenterId CostCenterId,
    [property: SwaggerSchema("Charged to this cost center, not yet due, in BRL.")]
    [property: SwaggerExample("18000.00")]
    decimal NotYetDue,
    [property: SwaggerSchema("Charged to this cost center, 1 to 30 days overdue, in BRL.")]
    [property: SwaggerExample("2400.00")]
    decimal Overdue1To30,
    [property: SwaggerSchema("Charged to this cost center, 31 to 60 days overdue, in BRL.")]
    [property: SwaggerExample("0.00")]
    decimal Overdue31To60,
    [property: SwaggerSchema("Charged to this cost center, 61 to 90 days overdue, in BRL.")]
    [property: SwaggerExample("0.00")]
    decimal Overdue61To90,
    [property: SwaggerSchema("Charged to this cost center, more than 90 days overdue, in BRL.")]
    [property: SwaggerExample("0.00")]
    decimal Overdue90Plus,
    [property: SwaggerSchema("Everything outstanding on this cost center, in BRL.")]
    [property: SwaggerExample("20400.00")]
    decimal Total);

[SwaggerSchema("The aging report for payables: what is outstanding, bucketed by how overdue it is, "
    + "with the same money sliced two ways. ByVendor and ByCostCenter are views of one set of "
    + "installments, not amounts to be added together.")]
public sealed record AgingReportResponse(
    [property: SwaggerSchema("Overall totals per bucket.")]
    AgingBucketTotals Totals,
    [property: SwaggerSchema("Breakdown by vendor.")]
    IReadOnlyList<AgingByVendorRow> ByVendor,
    [property: SwaggerSchema("Breakdown by cost center.")]
    IReadOnlyList<AgingByCostCenterRow> ByCostCenter);

[SwaggerSchema("One installment behind an aging bucket, for the drill-down.")]
public sealed record AgingItemResponse(
    PayableId PayableId,
    InstallmentId InstallmentId,
    VendorId VendorId,
    [property: SwaggerSchema("Vendor name as recorded on the payable.")]
    [property: SwaggerExample("Construtora Horizonte Ltda")]
    string CounterpartyName,
    [property: SwaggerSchema("Invoice number of the payable.")]
    [property: SwaggerExample("00012345")]
    string? DocumentNumber,
    [property: SwaggerSchema("Cost center the spend is charged to.")]
    CostCenterId CostCenterId,
    [property: SwaggerSchema("When the installment fell due.")]
    [property: SwaggerExample("2026-07-10")]
    DateOnly DueDate,
    [property: SwaggerSchema("Calendar days past the due date. Zero or negative when not yet due.")]
    [property: SwaggerExample("40")]
    int DaysOverdue,
    [property: SwaggerSchema("Still owed on this installment, in BRL — net of whatever was already paid, "
        + "not the original amount.")]
    [property: SwaggerExample("1430.25")]
    decimal Remaining,
    [property: SwaggerSchema("Which bucket the installment falls in, derived from DaysOverdue.")]
    AgingBucket Bucket);
