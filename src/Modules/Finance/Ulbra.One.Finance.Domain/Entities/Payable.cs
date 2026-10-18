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
    // When this payable was generated from a Budgeting Requisition (Origin = Purchase), the originating
    // requisition id — the idempotency/dedup key for the integration and the back-reference used to feed
    // "realizado" back to Budgeting. Null for payables created directly in Finance.
    public Guid? OriginRequisitionId { get; private set; }
    // The Budgeting unit (Unidade) the originating requisition belonged to. Finance has no Unit dimension of
    // its own, so this is stamped for Finance-side reporting only. Null unless requisition-sourced.
    public Guid? OriginUnitId { get; private set; }
    // The originating requisition's friendly sequential code (Requisition.Code), denormalized for display and
    // search on the Contábil review list (Finance can't read Budgeting entities). Null unless requisition-sourced.
    public int? OriginRequisitionCode { get; private set; }
    // For a recurring requisition (Conta Fixa), the competency ("YYYY-MM") of the monthly cycle
    // this payable materializes. Null = the base payable (from approval) OR a non-recurring/manual payable. The
    // uniqueness key is (OriginRequisitionId, OriginCycleCompetency) — one payable per requisition per cycle,
    // so a recurring requisition maps to N payables (AEL-877).
    public string? OriginCycleCompetency { get; private set; }
    // Conta Fixa (AEL-881): this invoice must carry its Nota Fiscal before it can be scheduled/paid. Set on a
    // requisition-sourced Payable whose requisition is a Conta Fixa; the auto-schedule path (PayableReceivedConsumer)
    // then parks it in PendingAccounting instead of scheduling without an NF. Default false = current behaviour.
    public bool RequiresFiscalDocument { get; private set; }
    // Serviço/Conta Fixa (análise tributária): this title's retenções must be validated by the Tributário before it
    // can be scheduled/paid or booked. Set on a requisition-sourced Payable whose requisition is Prestação de Serviço
    // or Conta Fixa; Approve() then routes it to PendingTaxAnalysis instead of PendingAccounting. Default false.
    public bool RequiresTaxAnalysis { get; private set; }
    // The Contábil user who clicked "Solicitar Nota Fiscal" for this payable — stamped so the requester's later
    // NF upload can notify them back that it arrived. Null when no request was made. (AEL-865)
    public Guid? FiscalDocumentRequestedBy { get; private set; }
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
    // The Tributário who released this payable from the análise tributária stage (CompleteTaxAnalysis). Null unless
    // the title passed through PendingTaxAnalysis.
    public string? TaxReviewedBy { get; private set; }
    public DateTimeOffset? TaxReviewedAt { get; private set; }
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
        IEnumerable<Installment> installments, Guid? unitId = null)
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
        OriginUnitId = unitId is { } u && u != Guid.Empty ? u : null;
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
        string description, Guid costCenterId, Guid? categoryId, IEnumerable<Installment> installments, Guid? unitId = null)
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
        // Manual unit tag (rastreio). A requisition-sourced unit is immutable — never overwrite it here.
        if (OriginRequisitionId is null)
            OriginUnitId = unitId is { } u && u != Guid.Empty ? u : null;
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
        // booked liability from the net actually paid. They stay editable through the review stages
        // (the Tributário corrects them during análise tributária) and lock only when the title is
        // released to Scheduled (CompleteTaxAnalysis / CompleteAccounting).
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
        // Approval no longer means "ready to pay". A Serviço/Conta Fixa title (RequiresTaxAnalysis) enters the
        // blocking análise tributária stage first, where the Tributário validates the retenções; every other title
        // enters the accounting stage. Routing here centralizes the invariant across auto-schedule + manual approval.
        Status = RequiresTaxAnalysis ? PayableStatus.PendingTaxAnalysis : PayableStatus.PendingAccounting;
        RejectionReason = null;
        ApprovedBy = approvedBy;
        ApprovedAt = at;
        RejectedBy = null;
        RejectedAt = null;
    }

    /// <summary>Accountant confirms the accounting review (retentions, ledger classification) and releases the
    /// payable for payment. Requires a registered fiscal document (Nota Fiscal) — checked by the caller, since the
    /// <c>FiscalDocument</c> is a separate aggregate — enforced here as an invariant. Advance payables
    /// (adiantamento) are exempt: they are paid before the NF exists. The requisition auto-schedule passes
    /// <paramref name="allowWithoutFiscalDocument"/> to schedule/pay without an NF (tracked as "NF pendente"
    /// until one is attached). Only valid from <see cref="PayableStatus.PendingAccounting"/>.</summary>
    public void CompleteAccounting(string bookedBy, DateTimeOffset at, bool hasFiscalDocument,
        bool allowWithoutFiscalDocument = false)
    {
        if (Status != PayableStatus.PendingAccounting)
            throw new InvalidOperationException("Only a payable pending accounting can be booked (scheduled).");
        if (!hasFiscalDocument && !allowWithoutFiscalDocument && Origin != PayableOrigin.Advance)
            throw new InvalidOperationException("A registered fiscal document (Nota Fiscal) is required before accounting can be completed.");
        Status = PayableStatus.Scheduled;
        AccountedBy = bookedBy;
        AccountedAt = at;
    }

    /// <summary>The Tributário confirms the análise tributária (retenções validated) and releases the payable for
    /// payment. This is the entry to <see cref="PayableStatus.Scheduled"/> for Serviço/Conta Fixa titles — it plays
    /// the role <see cref="CompleteAccounting"/> plays for the rest, so the caller publishes <c>PayableScheduled</c>
    /// (the accounting incurral) right after. Hard-gated on a registered Nota Fiscal (checked by the caller, since the
    /// FiscalDocument is a separate aggregate); Advance payables are exempt. Only valid from PendingTaxAnalysis.</summary>
    public void CompleteTaxAnalysis(string reviewedBy, DateTimeOffset at, bool hasFiscalDocument)
    {
        if (Status != PayableStatus.PendingTaxAnalysis)
            throw new InvalidOperationException("Only a payable pending tax analysis can be released for payment.");
        if (!hasFiscalDocument && Origin != PayableOrigin.Advance)
            throw new InvalidOperationException("A registered fiscal document (Nota Fiscal) is required before tax analysis can be completed.");
        Status = PayableStatus.Scheduled;
        TaxReviewedBy = reviewedBy;
        TaxReviewedAt = at;
        // Same scheduling stamps CompleteAccounting sets — this release is what books/schedules the title.
        AccountedBy = reviewedBy;
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

    /// <summary>Reopens a rejected payable (Rejected → Received) so a corrected + re-approved requisition can
    /// refresh it in place instead of creating a second title (AEL-935). Resets it to a fresh intake state:
    /// clears the rejection/approval stamps AND the accounting classification, so the corrected title rides the
    /// pipeline again and re-enters the Contábil review ("Registro de Despesa") to be re-classified — the queue
    /// keys on AccountingAccountId == null, and the correction may change what expense is incurred.</summary>
    public void Reopen()
    {
        if (Status != PayableStatus.Rejected)
            throw new InvalidOperationException("Only a rejected payable can be reopened.");
        Status = PayableStatus.Received;
        RejectionReason = null;
        RejectedBy = null;
        RejectedAt = null;
        ApprovedBy = null;
        ApprovedAt = null;
        // Reset the accounting classification so the re-approved title lands back in the review queue instead of
        // silently re-booking under the stale account (its incurral was reversed on the return).
        AccountingAccountId = null;
        SuggestedAccountingAccountId = null;
        ClassificationConfidence = null;
        ClassificationRationale = null;
        AccountedBy = null;
        AccountedAt = null;
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

    /// <summary>Stamps the Budgeting origin (requisition + unit) on a system-generated payable
    /// (<see cref="PayableOrigin.Purchase"/>). Set once at creation by the requisition→Finance consumer;
    /// <see cref="OriginRequisitionId"/> is the integration's idempotency key.</summary>
    public void SetOrigin(Guid requisitionId, Guid unitId, int? requisitionCode = null)
    {
        OriginRequisitionId = requisitionId == Guid.Empty ? null : requisitionId;
        OriginUnitId = unitId == Guid.Empty ? null : unitId;
        OriginRequisitionCode = requisitionCode;
    }

    /// <summary>Tags this payable as the monthly cycle of a recurring requisition (competency "YYYY-MM"); part of
    /// the (requisition, cycle) uniqueness key. Null/blank = the base payable from approval (AEL-877).</summary>
    public void SetCycle(string? competency) =>
        OriginCycleCompetency = string.IsNullOrWhiteSpace(competency) ? null : competency;

    /// <summary>Marks this requisition-sourced payable (base or cycle) as a Conta Fixa invoice that requires its
    /// Nota Fiscal before scheduling — the auto-schedule path parks it in PendingAccounting until one is uploaded
    /// (AEL-881). Idempotent; never un-sets (a Conta Fixa never becomes NF-optional).</summary>
    public void RequireFiscalDocument() => RequiresFiscalDocument = true;

    /// <summary>Marks this requisition-sourced payable (Prestação de Serviço or Conta Fixa) as requiring the
    /// Tributário's validation of its retenções before scheduling: Approve() then routes it to PendingTaxAnalysis
    /// instead of PendingAccounting. Idempotent; never un-sets.</summary>
    public void RequireTaxAnalysis() => RequiresTaxAnalysis = true;

    /// <summary>Records the Contábil user who requested this payable's Nota Fiscal ("Solicitar NF"), so the
    /// requester's later NF upload can notify them back that it arrived.</summary>
    public void MarkFiscalDocumentRequested(Guid requestedBy) =>
        FiscalDocumentRequestedBy = requestedBy == Guid.Empty ? null : requestedBy;

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

    /// <summary>Sets the data de emissão (which the incurral uses as the accounting competence). Called by the
    /// fiscal-document ingestion with the Nota Fiscal's own emission date, so an attached NF drives the issue
    /// date (overriding the default derived from the requisition's competency).</summary>
    public void SetIssueDate(DateOnly? issueDate)
    {
        EnsureMutable();
        IssueDate = issueDate;
    }

    /// <summary>Sets the número do documento (número da Nota Fiscal). Pure fiscal/accounting metadata — it feeds
    /// only the journal entry's SourceReference, never any amount/withholding/balance — so it stays editable in
    /// ANY state, including a PAID payable: Contábil frequently only receives the número after payment and must be
    /// able to complete the fiscal record. Called by the fiscal-document ingestion (propagating the parsed número)
    /// and by the Contábil review edit. Blank normalizes to null.</summary>
    public void SetDocumentNumber(string? documentNumber) =>
        DocumentNumber = string.IsNullOrWhiteSpace(documentNumber) ? null : documentNumber.Trim();

    /// <summary>Confirma "CPRB destacado na NF?" (indCPRB do R-2010; alterna INSS 3,5% × 11%). Editável até agendar;
    /// recalcular a retenção após alterar.</summary>
    public void SetCprbOnInvoice(bool? cprbOnInvoice)
    {
        EnsureMutable();
        CprbOnInvoice = cprbOnInvoice;
    }

    /// <summary>Sets (or clears) the accounting chart account that classifies this payable. Only valid before the
    /// payable schedules — once Scheduled/Paid the incurral is already posted, so the ledger account must move
    /// through <see cref="Reclassify"/> (which rebooks the Livro Razão) instead.</summary>
    public void Classify(Guid? accountingAccountId)
    {
        if (Status is PayableStatus.Scheduled or PayableStatus.Paid)
            throw new InvalidOperationException("A scheduled or paid payable must be reclassified (Reclassify), not classified.");
        AccountingAccountId = accountingAccountId;
        SuggestedAccountingAccountId = null;
        ClassificationConfidence = null;
        ClassificationRationale = null;
    }

    /// <summary>Contábil reclassifies the expense ledger account of an already-scheduled/paid payable (post-fato).
    /// Withholdings/liability are locked after Scheduled — only the expense account moves. Returns whether the
    /// account actually changed. The suggestion band is cleared regardless (even when confirming the same account),
    /// so the row always leaves the review queue; the caller publishes <c>PayableReclassified</c> either way so a
    /// parked incurral self-heals.</summary>
    public bool Reclassify(Guid? accountingAccountId)
    {
        if (Status is not (PayableStatus.Scheduled or PayableStatus.Paid))
            throw new InvalidOperationException("Only a scheduled or paid payable can be reclassified.");
        var changed = AccountingAccountId != accountingAccountId;
        AccountingAccountId = accountingAccountId;
        SuggestedAccountingAccountId = null;
        ClassificationConfidence = null;
        ClassificationRationale = null;
        return changed;
    }

    public Installment? FindInstallment(Guid installmentId) =>
        _installments.FirstOrDefault(i => i.Id == installmentId);

    public Payment? FindPayment(Guid paymentId) =>
        _installments.SelectMany(i => i.Payments).FirstOrDefault(p => p.Id == paymentId);

    public bool IsPayable => Status is PayableStatus.Scheduled or PayableStatus.Paid;

    public void RecomputeSettlement()
    {
        // Invariant: the canonical entries to Scheduled are CompleteAccounting / CompleteTaxAnalysis (they gate on
        // the Nota Fiscal and publish PayableScheduled → accounting incurral). This method only re-derives
        // Paid⇄Scheduled after a payment or its reversal on an already-scheduled payable — do NOT let it promote a
        // pre-Scheduled title (e.g. PendingTaxAnalysis, which has no payments), as that would skip the NF gate + the
        // event and bypass the blocking stage.
        if (Status is not (PayableStatus.Scheduled or PayableStatus.Paid)) return;
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
