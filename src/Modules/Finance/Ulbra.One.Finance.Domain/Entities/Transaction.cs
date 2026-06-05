namespace Ulbra.One.Finance.Domain.Entities;

/// <summary>Lançamento financeiro (conta a pagar/receber), classificado por Company × Unit × CostCenter.</summary>
public class Transaction
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CompanyId { get; private set; }
    public Guid UnitId { get; private set; }
    public Guid CostCenterId { get; private set; }

    public TransactionType Type { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public string Counterparty { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public DateOnly DueDate { get; private set; }

    public TransactionStatus Status { get; private set; } = TransactionStatus.Pending;
    public string? RejectionReason { get; private set; }

    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? ApprovedAt { get; private set; }
    public DateTime? PaidAt { get; private set; }

    private Transaction() { }

    public Transaction(Guid companyId, Guid unitId, Guid costCenterId, TransactionType type,
        string description, string counterparty, decimal amount, DateOnly dueDate)
    {
        CompanyId = companyId;
        UnitId = unitId;
        CostCenterId = costCenterId;
        Type = type;
        Description = description;
        Counterparty = counterparty;
        Amount = amount;
        DueDate = dueDate;
    }

    /// <summary>Aprova automaticamente despesas (a pagar) dentro do limite do centro de custo.</summary>
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
