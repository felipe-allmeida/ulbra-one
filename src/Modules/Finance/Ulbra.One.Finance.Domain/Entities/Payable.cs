using Ulbra.One.Common.Domain.Auditing;

namespace Ulbra.One.Finance.Domain.Entities;

/// <summary>Accounts-payable aggregate root: a payable with one or more installments and attachments.</summary>
public class Payable : IAuditable, ISoftDeletable
{
    private readonly List<Installment> _installments = new();
    private readonly List<PayableAttachment> _attachments = new();

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CompanyId { get; private set; }
    public PayableOrigin Origin { get; private set; }
    public Guid VendorId { get; private set; }
    public string CounterpartyName { get; private set; } = string.Empty;
    public string? DocumentNumber { get; private set; }
    public DateOnly? IssueDate { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public Guid CostCenterId { get; private set; }
    public Guid? CategoryId { get; private set; }
    public decimal TotalAmount { get; private set; }
    public PayableStatus Status { get; private set; } = PayableStatus.PendingApproval;
    public string? RejectionReason { get; private set; }
    public string? CancelReason { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;

    public IReadOnlyList<Installment> Installments => _installments;
    public IReadOnlyList<PayableAttachment> Attachments => _attachments;

    private Payable() { }

    public Payable(Guid companyId, PayableOrigin origin, Guid vendorId, string counterpartyName,
        string? documentNumber, DateOnly? issueDate, string description, Guid costCenterId, Guid? categoryId,
        IEnumerable<Installment> installments)
    {
        CompanyId = companyId;
        Origin = origin;
        VendorId = vendorId;
        CounterpartyName = counterpartyName;
        DocumentNumber = documentNumber;
        IssueDate = issueDate;
        Description = description;
        CostCenterId = costCenterId;
        CategoryId = categoryId;
        SetInstallments(installments);
    }

    /// <summary>Splits <paramref name="total"/> into <paramref name="count"/> installments (remainder on the
    /// last), with due dates stepping by <paramref name="intervalMonths"/> from <paramref name="firstDueDate"/>.</summary>
    public static List<Installment> GenerateInstallments(decimal total, int count, DateOnly firstDueDate, int intervalMonths)
    {
        if (count < 1) throw new ArgumentException("count must be >= 1", nameof(count));
        var each = Math.Round(total / count, 2, MidpointRounding.AwayFromZero);
        var rows = new List<Installment>(count);
        decimal accumulated = 0;
        for (var i = 0; i < count; i++)
        {
            var amount = i == count - 1 ? total - accumulated : each;
            accumulated += amount;
            rows.Add(new Installment(i + 1, firstDueDate.AddMonths(intervalMonths * i), amount));
        }
        return rows;
    }

    public void Update(Guid vendorId, string counterpartyName, string? documentNumber, DateOnly? issueDate,
        string description, Guid costCenterId, Guid? categoryId, IEnumerable<Installment> installments)
    {
        EnsureMutable();
        VendorId = vendorId;
        CounterpartyName = counterpartyName;
        DocumentNumber = documentNumber;
        IssueDate = issueDate;
        Description = description;
        CostCenterId = costCenterId;
        CategoryId = categoryId;
        SetInstallments(installments);
    }

    private void SetInstallments(IEnumerable<Installment> installments)
    {
        var list = installments.ToList();
        if (list.Count < 1) throw new ArgumentException("A payable needs at least one installment.", nameof(installments));
        _installments.Clear();
        _installments.AddRange(list);
        TotalAmount = list.Sum(i => i.Amount);
    }

    public void ApplyAutoApproval(decimal autoApprovalLimit)
    {
        if (Origin == PayableOrigin.Manual && autoApprovalLimit > 0 && TotalAmount <= autoApprovalLimit)
            Approve();
    }

    public void Approve()
    {
        if (Status is PayableStatus.Rejected or PayableStatus.Cancelled)
            throw new InvalidOperationException("Cannot approve a rejected or cancelled payable.");
        Status = PayableStatus.Approved;
        RejectionReason = null;
    }

    public void Reject(string? reason)
    {
        if (Status is PayableStatus.Settled or PayableStatus.Cancelled)
            throw new InvalidOperationException("Cannot reject a settled or cancelled payable.");
        Status = PayableStatus.Rejected;
        RejectionReason = reason;
    }

    public void Cancel(string? reason)
    {
        if (Status is PayableStatus.Settled or PayableStatus.Cancelled)
            throw new InvalidOperationException("Cannot cancel a settled or already-cancelled payable.");
        Status = PayableStatus.Cancelled;
        CancelReason = reason;
    }

    public void AddAttachment(PayableAttachment attachment) => _attachments.Add(attachment);

    private void EnsureMutable()
    {
        if (Status is not (PayableStatus.PendingApproval or PayableStatus.Approved))
            throw new InvalidOperationException("Only a pending or approved payable can be edited.");
    }

    public void MarkDeleted(DateTimeOffset at) => DeletedAt = at;
    public void MarkCreated(string userId, DateTimeOffset at) { CreatedBy = userId; CreatedAt = at; MarkUpdated(userId, at); }
    public void MarkUpdated(string userId, DateTimeOffset at) { UpdatedBy = userId; UpdatedAt = at; }
}
