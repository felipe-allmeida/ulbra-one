using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Finance.Domain;

namespace Ulbra.One.Finance.Application.Contracts;

public sealed record AgingBucketTotals(
    decimal NotYetDue, decimal Overdue1To30, decimal Overdue31To60, decimal Overdue61To90, decimal Overdue90Plus, decimal GrandTotal);

public sealed record AgingByVendorRow(
    VendorId VendorId, string Name, decimal NotYetDue, decimal Overdue1To30, decimal Overdue31To60,
    decimal Overdue61To90, decimal Overdue90Plus, decimal Total);

public sealed record AgingByCostCenterRow(
    CostCenterId CostCenterId, decimal NotYetDue, decimal Overdue1To30, decimal Overdue31To60,
    decimal Overdue61To90, decimal Overdue90Plus, decimal Total);

public sealed record AgingReportResponse(
    AgingBucketTotals Totals,
    IReadOnlyList<AgingByVendorRow> ByVendor,
    IReadOnlyList<AgingByCostCenterRow> ByCostCenter);

public sealed record AgingItemResponse(
    PayableId PayableId, InstallmentId InstallmentId, VendorId VendorId, string CounterpartyName,
    string? DocumentNumber, CostCenterId CostCenterId, DateOnly DueDate, int DaysOverdue,
    decimal Remaining, AgingBucket Bucket);
