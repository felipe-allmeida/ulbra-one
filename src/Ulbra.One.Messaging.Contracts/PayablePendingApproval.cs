namespace Ulbra.One.Messaging.Contracts;

/// <summary>Published when a Payable is created and lands in PendingApproval (not auto-approved).</summary>
public sealed record PayablePendingApproval
{
    public required Guid PayableId { get; init; }
    public required Guid CompanyId { get; init; }
    public required Guid CostCenterId { get; init; }
    public required string VendorName { get; init; }
    public required decimal TotalAmount { get; init; }
}
