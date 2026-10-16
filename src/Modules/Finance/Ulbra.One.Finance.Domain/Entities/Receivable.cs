using Ulbra.One.Common.Domain.Auditing;

namespace Ulbra.One.Finance.Domain.Entities;

/// <summary>Accounts-receivable aggregate root: a receivable with one or more installments.
/// Born ready to receive (<see cref="ReceivableStatus.AwaitingReceipt"/>) — no approval workflow, unlike Payable.</summary>
public class Receivable : IAuditable, ISoftDeletable
{
    private readonly List<ReceivableInstallment> _installments = new();

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CompanyId { get; private set; }
    public ReceivableOrigin Origin { get; private set; }
    public Guid PayerId { get; private set; }
    public string PayerName { get; private set; } = string.Empty;
    public string? DocumentNumber { get; private set; }
    public DateOnly? IssueDate { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public Guid CostCenterId { get; private set; }
    public Guid? RevenueCategoryId { get; private set; }
    public Guid? LedgerAccountId { get; private set; }
    public decimal TotalAmount { get; private set; }
    public ReceivableStatus Status { get; private set; } = ReceivableStatus.AwaitingReceipt;
    public string? CancelReason { get; private set; }
    public Guid? CancelledById { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public string? WriteOffReason { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;

    public IReadOnlyList<ReceivableInstallment> Installments => _installments;

    private Receivable() { }

    public Receivable(Guid companyId, ReceivableOrigin origin, Guid payerId, string payerName,
        string? documentNumber, DateOnly? issueDate, string description, Guid costCenterId, Guid? revenueCategoryId,
        Guid? ledgerAccountId, IEnumerable<ReceivableInstallment> installments)
    {
        CompanyId = companyId;
        Origin = origin;
        PayerId = payerId;
        PayerName = payerName;
        DocumentNumber = documentNumber;
        IssueDate = issueDate;
        Description = description;
        CostCenterId = costCenterId;
        RevenueCategoryId = revenueCategoryId;
        LedgerAccountId = ledgerAccountId;
        SetInstallments(installments);
    }

    /// <summary>Splits <paramref name="total"/> into <paramref name="count"/> installments (remainder on the
    /// last), with due dates stepping by <paramref name="intervalMonths"/> from <paramref name="firstDueDate"/>.</summary>
    public static List<ReceivableInstallment> GenerateInstallments(decimal total, int count, DateOnly firstDueDate, int intervalMonths)
    {
        if (count < 1) throw new ArgumentException("count must be >= 1", nameof(count));
        var each = Math.Round(total / count, 2, MidpointRounding.AwayFromZero);
        var rows = new List<ReceivableInstallment>(count);
        decimal accumulated = 0;
        for (var i = 0; i < count; i++)
        {
            var amount = i == count - 1 ? total - accumulated : each;
            accumulated += amount;
            rows.Add(new ReceivableInstallment(i + 1, firstDueDate.AddMonths(intervalMonths * i), amount));
        }
        return rows;
    }

    public void Update(Guid payerId, string payerName, string? documentNumber, DateOnly? issueDate,
        string description, Guid costCenterId, Guid? revenueCategoryId, Guid? ledgerAccountId,
        IEnumerable<ReceivableInstallment> installments)
    {
        EnsureMutable();
        PayerId = payerId;
        PayerName = payerName;
        DocumentNumber = documentNumber;
        IssueDate = issueDate;
        Description = description;
        CostCenterId = costCenterId;
        RevenueCategoryId = revenueCategoryId;
        LedgerAccountId = ledgerAccountId;
        SetInstallments(installments);
    }

    private void SetInstallments(IEnumerable<ReceivableInstallment> installments)
    {
        var list = installments.ToList();
        if (list.Count < 1) throw new ArgumentException("A receivable needs at least one installment.", nameof(installments));
        _installments.Clear();
        _installments.AddRange(list);
        TotalAmount = list.Sum(i => i.Amount);
    }

    public void Cancel(Guid cancelledById, DateTimeOffset at, string? reason)
    {
        if (Status is ReceivableStatus.Received or ReceivableStatus.Cancelled or ReceivableStatus.WrittenOff)
            throw new InvalidOperationException("Cannot cancel a received, cancelled or written-off receivable.");
        if (HasReceipts)
            throw new InvalidOperationException("Cannot cancel a receivable that already has receipts. Reverse them first.");
        Status = ReceivableStatus.Cancelled;
        CancelReason = reason;
        CancelledById = cancelledById;
        CancelledAt = at;
    }

    public void WriteOff(string? reason)
    {
        if (Status is ReceivableStatus.Received or ReceivableStatus.Cancelled or ReceivableStatus.WrittenOff)
            throw new InvalidOperationException("Cannot write off a received, cancelled or written-off receivable.");
        Status = ReceivableStatus.WrittenOff;
        WriteOffReason = reason;
    }

    public ReceivableInstallment? FindInstallment(Guid installmentId) =>
        _installments.FirstOrDefault(i => i.Id == installmentId);

    public Receipt? FindReceipt(Guid receiptId) =>
        _installments.SelectMany(i => i.Receipts).FirstOrDefault(r => r.Id == receiptId);

    public bool HasReceipts => _installments.SelectMany(i => i.Receipts).Any(r => !r.IsReversed);

    public bool IsReceivable => Status is ReceivableStatus.AwaitingReceipt or ReceivableStatus.Received;

    public void RecomputeSettlement()
    {
        if (Status is ReceivableStatus.Cancelled or ReceivableStatus.WrittenOff) return;
        Status = _installments.All(i => i.Status == ReceivableInstallmentStatus.Received)
            ? ReceivableStatus.Received
            : ReceivableStatus.AwaitingReceipt;
    }

    private void EnsureMutable()
    {
        if (Status != ReceivableStatus.AwaitingReceipt || HasReceipts)
            throw new InvalidOperationException("Only an awaiting-receipt receivable without receipts can be edited.");
    }

    public void MarkDeleted(DateTimeOffset at) => DeletedAt = at;
    public void MarkCreated(string userId, DateTimeOffset at) { CreatedBy = userId; CreatedAt = at; MarkUpdated(userId, at); }
    public void MarkUpdated(string userId, DateTimeOffset at) { UpdatedBy = userId; UpdatedAt = at; }
}
