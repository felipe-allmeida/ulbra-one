namespace Ulbra.One.Finance.Domain.Entities;

// TODO(AEL-579): legacy generic entity, superseded by the Payable aggregate for accounts payable.
// Kept for now — to be merged with an upcoming Receivable PR; decide then whether to delete it or
// promote it into a proper Receivable aggregate (with IAuditable/ISoftDeletable/TimeProvider, like Payable).
// Until then it does NOT honor audit/soft-delete conventions and uses DateTime.UtcNow — do not extend it.
/// <summary>Financial transaction (payable/receivable), classified by Company × Unit × CostCenter.</summary>
public class Transaction
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CompanyId { get; private set; }
    public Guid UnitId { get; private set; }
    public Guid CostCenterId { get; private set; }

    public TransactionType Type { get; private set; }
    public string Description { get; private set; } = string.Empty;

    /// <summary>Linked Registry Vendor (set for payables). Logical reference — no cross-context FK.</summary>
    public Guid? VendorId { get; private set; }
    /// <summary>Linked Registry Customer (set for receivables). Logical reference — no cross-context FK.</summary>
    public Guid? CustomerId { get; private set; }
    /// <summary>Snapshot of the linked Vendor/Customer name at creation time (stable history).</summary>
    public string CounterpartyName { get; private set; } = string.Empty;

    public decimal Amount { get; private set; }
    public DateOnly DueDate { get; private set; }

    public TransactionStatus Status { get; private set; } = TransactionStatus.Pending;
    public string? RejectionReason { get; private set; }

    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? ApprovedAt { get; private set; }
    public DateTime? PaidAt { get; private set; }

    private Transaction() { }

    public Transaction(Guid companyId, Guid unitId, Guid costCenterId, TransactionType type,
        string description, Guid? vendorId, Guid? customerId, string counterpartyName,
        decimal amount, DateOnly dueDate)
    {
        CompanyId = companyId;
        UnitId = unitId;
        CostCenterId = costCenterId;
        Type = type;
        Description = description;
        VendorId = vendorId;
        CustomerId = customerId;
        CounterpartyName = counterpartyName;
        Amount = amount;
        DueDate = dueDate;
    }

    /// <summary>Automatically approves expenses (payables) within the cost center's limit.</summary>
    public void ApplyAutoApproval(decimal autoApprovalLimit)
    {
        if (Type == TransactionType.Payable && autoApprovalLimit > 0 && Amount <= autoApprovalLimit)
            Approve();
    }

    public void Approve()
    {
        if (Status is TransactionStatus.Paid) return;
        Status = TransactionStatus.Approved;
        ApprovedAt = DateTime.UtcNow;
        RejectionReason = null;
    }

    public void Reject(string? reason)
    {
        Status = TransactionStatus.Rejected;
        RejectionReason = reason;
    }

    public void MarkAsPaid()
    {
        if (Status != TransactionStatus.Approved)
            throw new InvalidOperationException("Only an approved transaction can be paid.");
        Status = TransactionStatus.Paid;
        PaidAt = DateTime.UtcNow;
    }
}
