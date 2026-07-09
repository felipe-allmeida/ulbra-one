using Ulbra.One.Common.Domain.Auditing;

namespace Ulbra.One.Finance.Domain.Entities;

/// <summary>Accounts-payable aggregate root: a payable with one or more installments and attachments.</summary>
public class Payable : IAuditable, ISoftDeletable
{
    private readonly List<Installment> _installments = new();
    private readonly List<PayableAttachment> _attachments = new();
    private readonly List<PayableWithholding> _withholdings = new();

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
    public PayableStatus Status { get; private set; } = PayableStatus.Received;
    public string? RejectionReason { get; private set; }
    public string? CancelReason { get; private set; }
    public string? ApprovedBy { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public string? RejectedBy { get; private set; }
    public DateTimeOffset? RejectedAt { get; private set; }
    public string? AccountedBy { get; private set; }
    public DateTimeOffset? AccountedAt { get; private set; }
    public Guid? AccountingAccountId { get; private set; }
    public Guid? PaymentBankAccountId { get; private set; }
    public PaymentMethod? PaymentMethod { get; private set; }
    public Guid? SuggestedAccountingAccountId { get; private set; }
    public double? ClassificationConfidence { get; private set; }
    public string? ClassificationRationale { get; private set; }
    // Whether this payable is a Service (subject to source withholding) or a Product; null = undetermined
    // (no auto-calculation). This — not the LC116 subitem — is what triggers the automatic withholding.
    // Derived from the fiscal document (NFS-e=Service, NF-e=Product) or set on manual creation.
    public PayableType? Type { get; private set; }
    // "CPRB destacado na NF?" confirmado no lançamento/conferência — alterna a retenção de INSS (3,5% × 11%) e
    // vira o indCPRB do R-2010. null = ainda não confirmado (o cadastro do Fornecedor sugere o valor).
    public bool? CprbOnInvoice { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    public bool IsClassified => AccountingAccountId is not null;

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;

    public IReadOnlyList<Installment> Installments => _installments;
    public IReadOnlyList<PayableAttachment> Attachments => _attachments;
    public IReadOnlyList<PayableWithholding> Withholdings => _withholdings;

    public decimal TotalWithheld => _withholdings.Sum(w => w.Amount);
    public decimal NetTotal => TotalAmount - TotalWithheld;

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
        if (_installments.Any(i => i.Payments.Any(p => !p.IsReversed)))
            throw new InvalidOperationException("Cannot edit a payable that already has registered payments.");
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

    /// <summary>Replaces all withholdings (retenções) on this payable and recomputes each installment's
    /// <see cref="Installment.NetAmount"/>. Pass an empty sequence to clear all withholdings.</summary>
    public void SetWithholdings(IEnumerable<(WithholdingTaxType TaxType, decimal BaseAmount, decimal? Rate, decimal Amount)> items)
    {
        EnsureMutable();
        // Once scheduled, the accounting incurral (PayableScheduled → multi-line posting) has
        // already been booked against these withholdings; changing them now would desync the
        // booked liability from the net actually paid. They stay editable through PendingAccounting
        // (the accountant corrects them in "Registro de Despesa") and lock only when accounting is
        // completed (CompleteAccounting → Scheduled).
        if (Status == PayableStatus.Scheduled)
            throw new InvalidOperationException("Cannot change withholdings after the payable is scheduled.");
        if (_installments.Any(i => i.Payments.Any(p => !p.IsReversed)))
            throw new InvalidOperationException("Cannot change withholdings on a payable that already has registered payments.");

        var list = items.ToList();

        foreach (var item in list)
        {
            if (item.Amount <= 0)
                throw new InvalidOperationException("Each withholding amount must be greater than zero.");
        }

        var totalWithheld = list.Sum(x => x.Amount);
        if (totalWithheld >= TotalAmount)
            throw new InvalidOperationException("Total withheld cannot equal or exceed the payable's gross total amount.");

        _withholdings.Clear();
        foreach (var item in list)
            _withholdings.Add(new PayableWithholding(Id, item.TaxType, item.BaseAmount, item.Rate, item.Amount));

        RecomputeInstallmentNetAmounts();
    }

    private void RecomputeInstallmentNetAmounts()
    {
        var netTotal = NetTotal;

        if (_withholdings.Count == 0 || netTotal == TotalAmount)
        {
            foreach (var inst in _installments)
                inst.SetNetAmount(inst.Amount);
            return;
        }

        var ordered = _installments.OrderBy(i => i.Sequence).ToList();
        var runningSum = 0m;

        for (var idx = 0; idx < ordered.Count - 1; idx++)
        {
            var net = Math.Round(ordered[idx].Amount * netTotal / TotalAmount, 2);
            ordered[idx].SetNetAmount(net);
            runningSum += net;
        }

        // Last installment absorbs rounding to ensure Σ NetAmount == NetTotal exactly
        ordered[^1].SetNetAmount(netTotal - runningSum);
    }

    public void ApplyAutoApproval(decimal autoApprovalLimit, DateTimeOffset at)
    {
        // Never auto-schedule while the type is undetermined (Type is null): withholding is skipped for a
        // non-service, so the payable would schedule with zero retention and lock it in. A determined type is
        // required first — Service → federal withholding already computed; Product → nothing to withhold.
        // This is the automatic half of the "scheduled always withholds" guarantee.
        if (IsClassified && Type is not null && autoApprovalLimit > 0 && TotalAmount <= autoApprovalLimit
            && Status is PayableStatus.Received or PayableStatus.PendingClassification or PayableStatus.PendingApproval)
            Approve("system", at);
    }

    public void Approve(string approvedBy, DateTimeOffset at)
    {
        if (Status is PayableStatus.Rejected or PayableStatus.Cancelled or PayableStatus.Paid)
            throw new InvalidOperationException("Cannot approve a rejected, cancelled or paid payable.");
        // Approval no longer means "ready to pay" — the payable now enters the accounting stage, where the
        // accountant verifies retentions/vendor data and registers the Nota Fiscal before it can be scheduled.
        Status = PayableStatus.PendingAccounting;
        RejectionReason = null;
        ApprovedBy = approvedBy;
        ApprovedAt = at;
        RejectedBy = null;
        RejectedAt = null;
    }

    /// <summary>Accountant confirms the accounting review (retentions, ledger classification) and releases the
    /// payable for payment. Requires a registered fiscal document (Nota Fiscal) — checked by the caller, since the
    /// <c>FiscalDocument</c> is a separate aggregate — enforced here as an invariant. Only valid from
    /// <see cref="PayableStatus.PendingAccounting"/>.</summary>
    public void CompleteAccounting(string bookedBy, DateTimeOffset at, bool hasFiscalDocument)
    {
        if (Status != PayableStatus.PendingAccounting)
            throw new InvalidOperationException("Only a payable pending accounting can be booked (scheduled).");
        if (!hasFiscalDocument)
            throw new InvalidOperationException("A registered fiscal document (Nota Fiscal) is required before accounting can be completed.");
        Status = PayableStatus.Scheduled;
        AccountedBy = bookedBy;
        AccountedAt = at;
    }

    public void Reject(string? reason, string rejectedBy, DateTimeOffset at)
    {
        if (Status is PayableStatus.Paid or PayableStatus.Cancelled)
            throw new InvalidOperationException("Cannot reject a paid or cancelled payable.");
        Status = PayableStatus.Rejected;
        RejectionReason = reason;
        RejectedBy = rejectedBy;
        RejectedAt = at;
    }

    public void Cancel(string? reason)
    {
        if (Status is PayableStatus.Paid or PayableStatus.Cancelled)
            throw new InvalidOperationException("Cannot cancel a paid or already-cancelled payable.");
        // A partially-paid payable must have its payments reversed (estornadas) first: cancelling here would
        // reverse the accounting incurral while the settlement entries linger, leaving the liability unbalanced
        // (the human resolves by reversing the payment, then cancelling).
        if (_installments.Any(i => i.Payments.Any(p => !p.IsReversed)))
            throw new InvalidOperationException("Cannot cancel a payable that has registered payments; reverse the payments first.");
        Status = PayableStatus.Cancelled;
        CancelReason = reason;
    }

    public void AddAttachment(PayableAttachment attachment) => _attachments.Add(attachment);

    public void MarkPendingClassification() => Status = PayableStatus.PendingClassification;
    public void MarkPendingApproval() => Status = PayableStatus.PendingApproval;

    /// <summary>Persists the AI's suggestion for the human-review band; cleared once classified.</summary>
    public void SetSuggestion(Guid? accountingAccountId, double confidence, string? rationale)
    {
        SuggestedAccountingAccountId = accountingAccountId;
        ClassificationConfidence = confidence;
        ClassificationRationale = rationale;
    }

    /// <summary>Optional per-payable override of the auto-pay source (account + method). Null = use the company default.</summary>
    public void SetPaymentSource(Guid? bankAccountId, PaymentMethod? method)
    {
        PaymentBankAccountId = bankAccountId;
        PaymentMethod = method;
    }

    /// <summary>Marks whether this payable is a Service (subject to source withholding), a Product, or undetermined
    /// (null). This — not the LC116 subitem — is what triggers the automatic withholding calculation; the subitem
    /// only refines specific taxes (INSS labor supply, ISS by municipality).</summary>
    public void SetType(PayableType? type)
    {
        EnsureMutable();
        Type = type;
    }

    /// <summary>Confirma "CPRB destacado na NF?" (indCPRB do R-2010; alterna INSS 3,5% × 11%). Editável até agendar;
    /// recalcular a retenção após alterar.</summary>
    public void SetCprbOnInvoice(bool? cprbOnInvoice)
    {
        EnsureMutable();
        CprbOnInvoice = cprbOnInvoice;
    }

    /// <summary>Sets (or clears) the accounting chart account that classifies this payable.</summary>
    public void Classify(Guid? accountingAccountId)
    {
        AccountingAccountId = accountingAccountId;
        SuggestedAccountingAccountId = null;
        ClassificationConfidence = null;
        ClassificationRationale = null;
    }

    public Installment? FindInstallment(Guid installmentId) =>
        _installments.FirstOrDefault(i => i.Id == installmentId);

    public Payment? FindPayment(Guid paymentId) =>
        _installments.SelectMany(i => i.Payments).FirstOrDefault(p => p.Id == paymentId);

    public bool IsPayable => Status is PayableStatus.Scheduled or PayableStatus.Paid;

    public void RecomputeSettlement()
    {
        if (Status is PayableStatus.Rejected or PayableStatus.Cancelled) return;
        // Invariant: the canonical entry to Scheduled is CompleteAccounting (gates on the Nota Fiscal and
        // publishes PayableScheduled → accounting incurral). This method only re-derives Paid⇄Scheduled after a
        // payment or its reversal on an already-scheduled payable (reachable from Scheduled/Paid, never from
        // PendingAccounting) — do NOT call it to schedule a payable, as it would skip the NF gate + the event.
        Status = _installments.All(i => i.Status == InstallmentStatus.Paid)
            ? PayableStatus.Paid
            : PayableStatus.Scheduled;
    }

    private void EnsureMutable()
    {
        if (Status is PayableStatus.Paid or PayableStatus.Rejected or PayableStatus.Cancelled)
            throw new InvalidOperationException("Only a payable that is not paid/rejected/cancelled can be edited.");
    }

    public void MarkDeleted(DateTimeOffset at) => DeletedAt = at;
    public void MarkCreated(string userId, DateTimeOffset at) { CreatedBy = userId; CreatedAt = at; MarkUpdated(userId, at); }
    public void MarkUpdated(string userId, DateTimeOffset at) { UpdatedBy = userId; UpdatedAt = at; }
}
