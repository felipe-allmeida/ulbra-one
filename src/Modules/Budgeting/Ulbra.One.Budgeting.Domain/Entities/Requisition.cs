using Ulbra.One.Common.Domain;
using Ulbra.One.Common.Domain.Auditing;
using Ulbra.One.Common.Domain.Validation;
using CompetencyFormat = Ulbra.One.Common.Domain.Competency;

namespace Ulbra.One.Budgeting.Domain.Entities;

/// <summary>
/// A purchase requisition — the "head" document of the expense flow (Purchase / Service Provision /
/// Conta Fixa / Project). Carries the budget dimensions (<see cref="UnitId"/> ×
/// <see cref="CostCenterId"/>) and the competency window that the budget panel uses to compute
/// budgeted × consumed. References company/unit/cost center by loose Guid (no cross-schema FK).
/// Lifecycle is a guarded state machine (<see cref="RequisitionStatus"/>); the friendly
/// <see cref="Code"/> is a per-company sequential number, separate from the typed id.
/// </summary>
public sealed class Requisition : IAuditable
{
    private readonly List<RequisitionAttachment> _attachments = new();
    private readonly List<RequisitionTransition> _transitions = new();
    private readonly List<RequisitionPayment> _payments = new();
    private readonly List<RequisitionInstallment> _installments = new();
    private readonly List<RequisitionCycle> _cycles = new();

    /// <summary>System actor for payment-driven transitions (no human user behind a Finance event).</summary>
    private static readonly Guid SystemActor = Guid.Empty;

    public Guid Id { get; private set; } = Guid.NewGuid();

    /// <summary>Per-company sequential display number (friendly code), assigned at creation.</summary>
    public int Code { get; private set; }

    public Guid CompanyId { get; private set; }
    public Guid UnitId { get; private set; }
    public Guid CostCenterId { get; private set; }

    /// <summary>Optional link to a <see cref="Project"/> whose accumulated ceiling this requisition
    /// consumes. Loose Guid — no cross-schema FK, same as the other dimensions.</summary>
    public Guid? ProjectId { get; private set; }

    /// <summary>The vendor to be paid — a loose Guid into the Registry (no cross-schema FK).
    /// Required at creation (parity with the legacy app); stamped onto the generated Payable (AEL-793).</summary>
    public Guid VendorId { get; private set; }

    public RequisitionType Type { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public string? Observation { get; private set; }

    /// <summary>Estimated value. Non-recurring: the requisition's TOTAL — the per-competency
    /// (<see cref="EstimatedContributionFor"/>) and per-installment (<see cref="BuildPaymentSchedule"/>) splits
    /// divide it with the residual cents on the last part, so no money is lost to rounding. Recurring
    /// (Conta Fixa): the PER-MONTH value that repeats over the window — never divided
    /// (<see cref="SetDetails"/> enforces the Monthly/no-count plan that keeps this semantic, AEL-877).
    /// Never zero/negative; rounded to 2 decimals.</summary>
    public decimal EstimateValue { get; private set; }
    public EstimateType EstimateType { get; private set; }

    /// <summary>First competency the requisition touches (<c>"YYYY-MM"</c>).</summary>
    public string StartCompetency { get; private set; } = string.Empty;

    /// <summary>Last competency (<c>"YYYY-MM"</c>); <c>null</c> = open-ended (Conta Fixa / ongoing).</summary>
    public string? EndCompetency { get; private set; }

    public PaymentFrequency? PaymentFrequency { get; private set; }
    public int? PaymentInstallmentsCount { get; private set; }

    /// <summary>First installment due date — the anchor from which the generated Payable's installment dates
    /// step by <see cref="PaymentFrequency"/> (AEL-793). Required at creation (parity with the legacy app).</summary>
    public DateOnly FirstDueDate { get; private set; }

    /// <summary>Issue (emission) date informed in the financial-complement step (AEL-844) — a single date for
    /// the whole complement (not per installment). Flows to the generated Payable's IssueDate/competency. Null
    /// until the financial complement is submitted.</summary>
    public DateOnly? IssueDate { get; private set; }

    public RequisitionStatus Status { get; private set; } = RequisitionStatus.Draft;

    /// <summary>Value the triage vetted, snapshotted at <see cref="ApproveTriage"/>. If the financial
    /// complement raises <see cref="EstimateValue"/> above this, the requisition returns to triage for
    /// re-approval. Null until the requisition has been triaged.</summary>
    public decimal? TriageApprovedValue { get; private set; }

    /// <summary>Vendor the triage vetted, snapshotted at <see cref="ApproveTriage"/>. If the financial
    /// complement changes the vendor, the requisition returns to triage (the vendor check must be redone).
    /// Null until the requisition has been triaged.</summary>
    public Guid? TriageApprovedVendorId { get; private set; }

    public IReadOnlyList<RequisitionAttachment> Attachments => _attachments;

    /// <summary>Authored payment plan (AEL-844), captured in the financial-complement step. Empty until the
    /// requester submits it; once present it is the source of truth for <see cref="BuildPaymentSchedule"/>.</summary>
    public IReadOnlyList<RequisitionInstallment> Installments => _installments;

    /// <summary>Monthly cycles launched manually after approval for a recurring requisition (AEL-877) — each is
    /// an invoice for a competency that materializes its own Payable. Empty for non-recurring requisitions.</summary>
    public IReadOnlyList<RequisitionCycle> Cycles => _cycles;

    /// <summary>The Payable created from the approval — the "base cycle" (competency = <see cref="StartCompetency"/>).
    /// Filled by the Finance return callback (AEL-877); null until Finance confirms it. Cycle Payables live on
    /// <see cref="RequisitionCycle.PayableId"/>.</summary>
    public Guid? BasePayableId { get; private set; }

    /// <summary>Chronological trail of status transitions (who/when/from→to) — audit + future SLA base.</summary>
    public IReadOnlyList<RequisitionTransition> Transitions => _transitions;

    /// <summary>Real payments (settlements) fed back from Finance — the "realized" source (AEL-793).</summary>
    public IReadOnlyList<RequisitionPayment> Payments => _payments;

    /// <summary>Realized: the sum of non-reversed real payments (replaces the estimate-based proxy).</summary>
    public decimal RealizedValue => _payments.Where(p => !p.Reversed).Sum(p => p.Amount);

    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;

    /// <summary>When the requisition entered its CURRENT status (Draft = creation). Base for the stage
    /// SLA on the list/detail — a scalar, so it's available without loading the transition trail.</summary>
    public DateTimeOffset StatusChangedAt { get; private set; }

    private Requisition() { }

    public Requisition(
        Guid companyId, Guid unitId, Guid costCenterId, int code,
        RequisitionType type, string description, decimal estimateValue, EstimateType estimateType,
        string startCompetency, Guid vendorId, DateOnly firstDueDate,
        string? endCompetency = null, string? observation = null,
        PaymentFrequency? paymentFrequency = null, int? paymentInstallmentsCount = null)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
        if (unitId == Guid.Empty) throw new ArgumentException("UnitId is required.", nameof(unitId));
        if (costCenterId == Guid.Empty) throw new ArgumentException("CostCenterId is required.", nameof(costCenterId));
        if (code < 1) throw new ArgumentOutOfRangeException(nameof(code), "Code must be a positive sequential number.");

        CompanyId = companyId;
        UnitId = unitId;
        CostCenterId = costCenterId;
        Code = code;
        Type = type;
        Status = RequisitionStatus.Draft;

        SetDetails(type, description, estimateValue, estimateType, startCompetency, vendorId, firstDueDate, endCompetency, observation, paymentFrequency, paymentInstallmentsCount);
    }

    /// <summary>Updates the editable fields. Dimensions (company/unit/cost center), <see cref="Code"/> and
    /// <see cref="Status"/> are immutable. Allowed only while Draft or sent back for Complement.</summary>
    public void Update(
        RequisitionType type, string description, decimal estimateValue, EstimateType estimateType,
        string startCompetency, Guid vendorId, DateOnly firstDueDate, string? endCompetency, string? observation,
        PaymentFrequency? paymentFrequency, int? paymentInstallmentsCount)
    {
        EnsureEditable();
        SetDetails(type, description, estimateValue, estimateType, startCompetency, vendorId, firstDueDate, endCompetency, observation, paymentFrequency, paymentInstallmentsCount);
    }

    public void AddAttachment(RequisitionAttachment attachment) => _attachments.Add(attachment);

    /// <summary>Links this requisition to a project, so its cost counts against that project's
    /// accumulated ceiling. Allowed only while the requisition is still editable.</summary>
    public void LinkToProject(Guid projectId)
    {
        if (projectId == Guid.Empty) throw new ArgumentException("ProjectId is required.", nameof(projectId));
        EnsureEditable();
        ProjectId = projectId;
    }

    /// <summary>Removes the project link. Allowed only while the requisition is still editable.</summary>
    public void UnlinkProject()
    {
        EnsureEditable();
        ProjectId = null;
    }

    // ---- State machine ---------------------------------------------------------------------------
    // Every transition records who/when/from→to on the aggregate (see Transitions). Contextual inputs
    // (actor, timestamp, budget verdict) are passed in by the endpoint — the domain never reads
    // UserContext / TimeProvider / roles / permissions / Core types.

    /// <summary>Requester submits a Draft — always moves to Triage. There are no shortcuts: every
    /// requisition is triaged, receives its financial complement, and is approved by the Approver
    /// (no requester autonomy, no cost-center auto-approval — AEL-841).</summary>
    public void Submit(Guid actorId, DateTimeOffset at)
    {
        EnsureFrom(RequisitionStatus.Draft);
        EnsureBoletoLinesValid();
        MoveTo(RequisitionStatus.Triage, RequisitionAction.Submit, actorId, at, null);
    }

    /// <summary>Guards Draft→Triage: every boleto installment needs a well-formed linha digitável (47/48). A
    /// draft may hold an incomplete line (the AI reader can drop a digit); it just can't advance until fixed.</summary>
    private void EnsureBoletoLinesValid()
    {
        if (_installments.Any(i => i.PaymentMethod == PaymentMethod.Boleto && !BoletoLine.IsValid(i.BoletoDigitableLine)))
            throw new RequisitionException(RequisitionErrors.BoletoDigitableLineInvalid);
    }

    /// <summary>Controllership approves the triage (having vetted vendor/cost center/unit). Snapshots the vetted
    /// value and vendor so that a raise or vendor swap in the financial-complement step routes back to triage.
    /// When the requester front-loaded the financial plan (installments + emission date) before triage, the
    /// complement step is skipped and the requisition goes straight to Approval — triage has just
    /// vetted the CURRENT value (= Σ installments) and vendor, so there is nothing left to re-check. Otherwise it
    /// returns to the requester for the classic financial-complement step.</summary>
    public void ApproveTriage(Guid actorId, DateTimeOffset at)
    {
        EnsureFrom(RequisitionStatus.Triage);
        TriageApprovedValue = EstimateValue;
        TriageApprovedVendorId = VendorId;
        var target = _installments.Count > 0 && IssueDate is not null
            ? RequisitionStatus.Approval
            : RequisitionStatus.FinancialComplement;
        MoveTo(target, RequisitionAction.ApproveTriage, actorId, at, null);
    }

    /// <summary>Requester submits the financial complement (AEL-844): the authored installment plan (due date,
    /// value to transfer and payment method per installment) plus the single emission date. The authored
    /// installments become the source of truth for the generated Payable, and their sum becomes the
    /// requisition's value (the "programmed total"). Moves to Approval when that total did not rise above the
    /// triage-approved amount and the vendor is unchanged; otherwise returns to Triage for re-approval (the
    /// triage vetting must be redone). Fiscal documents are optional and attached separately.</summary>
    public void SubmitFinancialComplement(
        Guid actorId, DateTimeOffset at, DateOnly? issueDate,
        IReadOnlyList<(DateOnly DueDate, decimal Amount, PaymentMethod Method, string? BoletoDigitableLine, string? PixKeyOverride)> installments)
    {
        EnsureFrom(RequisitionStatus.FinancialComplement);
        ApplyFinancialPlan(issueDate, installments);

        // The Σ-overwrite in ApplyFinancialPlan compares back to the triage-approved amount, so a raise (or a
        // vendor swap) routes back to triage for re-approval (AEL-844).
        var valueRaised = TriageApprovedValue is decimal vetted && EstimateValue > vetted;
        var vendorChanged = TriageApprovedVendorId is Guid vettedVendor && VendorId != vettedVendor;
        if (valueRaised || vendorChanged)
            MoveTo(RequisitionStatus.Triage, RequisitionAction.ReturnToTriageForRevalue, actorId, at,
                valueRaised ? "value raised above triage-approved amount" : "vendor changed after triage");
        else
            MoveTo(RequisitionStatus.Approval, RequisitionAction.SubmitFinancialComplement, actorId, at, null);
    }

    /// <summary>Front-loads the financial plan (installments + single emission date) at creation/edit, BEFORE
    /// triage. Same validation as the financial complement, but it does NOT transition — it only
    /// stores the plan while the requisition is still editable (Draft / Complement / FinancialComplement). Honors
    /// the Conta Fixa single-installment rule via <see cref="ApplyFinancialPlan"/>. Once the plan is complete,
    /// <see cref="ApproveTriage"/> skips the FinancialComplement stage.</summary>
    public void SetFinancialPlan(
        DateOnly? issueDate,
        IReadOnlyList<(DateOnly DueDate, decimal Amount, PaymentMethod Method, string? BoletoDigitableLine, string? PixKeyOverride)> installments)
    {
        EnsureEditable();
        ApplyFinancialPlan(issueDate, installments);
    }

    /// <summary>Drops a previously front-loaded plan — used when the requester unchecks "inform the financial part
    /// now" on edit. Does NOT touch <see cref="EstimateValue"/>: on the same Update, SetDetails ran
    /// first and already fixed the typed estimate, which is the authority once the plan is gone.</summary>
    public void ClearFinancialPlan()
    {
        EnsureEditable();
        _installments.Clear();
        IssueDate = null;
    }

    /// <summary>Validates and applies an authored financial plan (installments + single emission date) WITHOUT
    /// transitioning: clears/re-adds the 1-based installments, sets <see cref="IssueDate"/>, and overwrites
    /// <see cref="EstimateValue"/> with their sum (the "programmed total"). Shared by the classic financial
    /// complement (which then routes) and the front-load path (<see cref="SetFinancialPlan"/>).</summary>
    private void ApplyFinancialPlan(
        DateOnly? issueDate,
        IReadOnlyList<(DateOnly DueDate, decimal Amount, PaymentMethod Method, string? BoletoDigitableLine, string? PixKeyOverride)> installments)
    {
        if (installments is null || installments.Count == 0)
            throw new RequisitionException(RequisitionErrors.InstallmentsRequired);
        // Recurring: the plan authors the FIRST cycle only (one invoice = one installment = one boleto); later
        // months are launched as cycles. Also keeps EstimateValue == the monthly value — the Σ-overwrite below
        // would silently turn several rows into a "total" and inflate the per-month forecast (AEL-877).
        if (IsRecurring && installments.Count != 1)
            throw new RequisitionException(RequisitionErrors.RecurringSingleInstallment);
        // The emission date is required (a null/omitted one deserializes as 0001-01-01) and cannot be later than
        // the first due date — it becomes the Payable's accounting competency (AEL-844).
        if (issueDate is not DateOnly issue || issue == default)
            throw new RequisitionException(RequisitionErrors.IssueDateRequired);
        if (issue > installments.Min(i => i.DueDate))
            throw new RequisitionException(RequisitionErrors.IssueDateAfterFirstDueDate);

        _installments.Clear();
        var sequence = 1;
        foreach (var i in installments)
            _installments.Add(new RequisitionInstallment(Id, sequence++, i.DueDate, i.Amount, i.Method, i.BoletoDigitableLine, i.PixKeyOverride));

        IssueDate = issue;
        EstimateValue = NormalizeEstimate(_installments.Sum(i => i.Amount));
    }

    /// <summary>Sends the requisition back to the requester for complement (from triage or approval).</summary>
    public void ReturnForComplement(Guid actorId, DateTimeOffset at, string? reason = null) =>
        Transition(RequisitionStatus.Complement, RequisitionAction.ReturnForComplement, actorId, at, reason,
            RequisitionStatus.Triage, RequisitionStatus.Approval);

    /// <summary>The Aprovador returns a front-loaded requisition to the requester specifically to revise the
    /// financial plan (parcelas/emissão) without redoing triage. Mirrors
    /// <see cref="ReturnForComplement"/> but targets FinancialComplement from Approval.</summary>
    public void ReturnToFinancialComplement(Guid actorId, DateTimeOffset at, string? reason = null) =>
        Transition(RequisitionStatus.FinancialComplement, RequisitionAction.ReturnToFinancialComplement, actorId, at, reason,
            RequisitionStatus.Approval);

    /// <summary>Requester resubmits after complement — re-enters triage (any prior approval is discarded).</summary>
    public void Resubmit(Guid actorId, DateTimeOffset at) =>
        Transition(RequisitionStatus.Triage, RequisitionAction.Resubmit, actorId, at, null, RequisitionStatus.Complement);

    /// <summary>Approver approves for payment. There is no alçada (authorization limit) — this
    /// approver has unlimited authority (AEL-841). The budget ceiling is the only guard and is a HARD block
    /// with no override: a breach must be resolved by an Adequação Orçamentária that raises the ceiling, not
    /// by overriding here — EXCEPT a requisition that <see cref="BypassesBudgetCeiling"/> (Conta Fixa, AEL-881),
    /// where the breach is ignored. The caller computes <paramref name="candidateExceeds"/> (it needs the budget
    /// read model / DB); the domain owns the bypass rule so the exemption lives in one place.</summary>
    public void Approve(Guid actorId, DateTimeOffset at, bool candidateExceeds = false)
    {
        EnsureFrom(RequisitionStatus.Approval);
        var blocked = candidateExceeds && !BypassesBudgetCeiling;
        if (blocked)
            throw new RequisitionException(RequisitionErrors.BudgetExceeded);
        MoveTo(RequisitionStatus.InPayment, RequisitionAction.Approve, actorId, at, null);
    }

    // Payment-derived transitions driven by the Finance integration (later issue) — no endpoints here yet,
    // but they record history uniformly so the trail is complete once wired.
    public void MarkAwaitingFiscalDocument(Guid actorId, DateTimeOffset at) =>
        Transition(RequisitionStatus.AwaitingFiscalDocument, RequisitionAction.MarkAwaitingFiscalDocument, actorId, at, null, RequisitionStatus.InPayment);
    public void MarkPaid(Guid actorId, DateTimeOffset at) =>
        Transition(RequisitionStatus.Paid, RequisitionAction.MarkPaid, actorId, at, null, RequisitionStatus.InPayment, RequisitionStatus.AwaitingFiscalDocument);
    public void MarkFinalized(Guid actorId, DateTimeOffset at) =>
        Transition(RequisitionStatus.Finalized, RequisitionAction.MarkFinalized, actorId, at, null, RequisitionStatus.Paid);

    /// <summary>Reopens a Paid requisition when a settling payment was reversed and it's no longer fully
    /// covered (auto-heal, mirrors the legacy status reconciler). Driven by the Finance payment-reversed event.</summary>
    public void ReopenForPayment(Guid actorId, DateTimeOffset at) =>
        Transition(RequisitionStatus.InPayment, RequisitionAction.ReopenForPayment, actorId, at, "payment reversed", RequisitionStatus.Paid);

    /// <summary>Finance rejected the generated title: return the requisition to the requester (Complement) or
    /// controllership (Triage), carrying the rejection reason (AEL-935). Driven by the PayableRejected event.</summary>
    public void ReturnFromPayment(RequisitionReturnTarget target, Guid actorId, DateTimeOffset at, string? reason)
    {
        var to = target == RequisitionReturnTarget.Controllership ? RequisitionStatus.Triage : RequisitionStatus.Complement;
        Transition(to, RequisitionAction.ReturnFromPayment, actorId, at, reason,
            RequisitionStatus.InPayment, RequisitionStatus.AwaitingFiscalDocument);
    }

    // ---- Finance payment feedback (AEL-793) ------------------------------------------------------
    // Fed by the Budgeting consumer of Finance's PaymentRegistered/PaymentReversed events. Idempotent
    // (dedup by PaymentId — at-least-once delivery). Real payments become the "realized" amount.

    /// <summary>Whether a real payment should settle the requisition (→ Paid). Non-recurring: when the realized
    /// amount covers the total. Recurring (Conta Fixa): only once TERMINATED (<see cref="EndCompetency"/>
    /// set) AND every cycle is covered — an OPEN recurrence never settles by payment (stays InPayment for the
    /// next month's invoice, mirrors the legacy status reconciler).</summary>
    private bool ShouldSettle() =>
        !IsRecurring ? RealizedValue + 0.005m >= TotalCost()
        : EndCompetency is not null && AllCyclesSettled();

    /// <summary>Total due for a recurrence: the base cycle (<see cref="EstimateValue"/> — the approval Payable)
    /// plus every manually-launched cycle. Base for settling a TERMINATED recurrence.</summary>
    private decimal TotalCyclesDue() => EstimateValue + _cycles.Sum(c => c.Amount);

    /// <summary>Whether the realized amount covers the base cycle + all launched cycles of a recurrence. The aggregate
    /// comparison is sound because payments are bounded upstream (a Finance installment rejects principal above
    /// its outstanding), so the gross realizado can only reach the sum when EVERY Payable is fully paid — one
    /// month can't mask another. A cancelled cycle Payable stops counting because its row is backed out by the
    /// <c>PayableCancelled</c> return path (<see cref="RemoveCycleForCancelledPayable"/>).</summary>
    private bool AllCyclesSettled() => RealizedValue + 0.005m >= TotalCyclesDue();

    /// <summary>Records a real payment applied to this requisition (from Finance). Idempotent on
    /// <paramref name="paymentId"/>. When <see cref="ShouldSettle"/> holds and the requisition is still awaiting
    /// payment, it advances to <see cref="RequisitionStatus.Paid"/>.</summary>
    public void RegisterPayment(Guid paymentId, Guid payableId, decimal amount, string competency, DateTimeOffset paidAt)
    {
        if (_payments.Any(p => p.PaymentId == paymentId)) return; // already applied — at-least-once safety
        _payments.Add(new RequisitionPayment(Id, paymentId, payableId, amount, CompetencyFormat.Normalize(competency), paidAt));

        if (Status is RequisitionStatus.InPayment or RequisitionStatus.AwaitingFiscalDocument && ShouldSettle())
            MarkPaid(SystemActor, paidAt);
    }

    /// <summary>Backs out a previously-recorded payment (Finance reversal). Idempotent. If the requisition had
    /// been marked Paid and is no longer fully covered, it reopens to <see cref="RequisitionStatus.InPayment"/>.</summary>
    public void ReversePayment(Guid paymentId, DateTimeOffset at)
    {
        var payment = _payments.FirstOrDefault(p => p.PaymentId == paymentId && !p.Reversed);
        if (payment is null) return; // unknown or already reversed
        payment.Reverse();

        // No longer fully covered → reopen. Uses ShouldSettle so it holds for both the non-recurring total and a
        // terminated recurrence's cycle total (an open recurrence is never Paid, so this is a no-op there).
        if (Status == RequisitionStatus.Paid && !ShouldSettle())
            ReopenForPayment(SystemActor, at);
    }

    // ---- Recurrence cycles (Conta Fixa), AEL-877 -----------------------------------

    /// <summary>Links the base Payable (from approval) once Finance confirms its creation (idempotent).</summary>
    public void LinkBasePayable(Guid payableId) => BasePayableId ??= payableId;

    /// <summary>Launches the invoice of a month for a recurring requisition already in payment: a new cycle
    /// (its own Payable is materialized downstream) WITHOUT re-running the workflow — the initial approval covers
    /// the future invoices (mirrors the legacy app). The competency must be after the start (the base owns the
    /// start), within the window if terminated, and not already launched.</summary>
    public RequisitionCycle AddCycle(string competency, decimal amount, DateOnly dueDate, PaymentMethod method,
        string? boletoDigitableLine, Guid actorId, DateTimeOffset at, string? pixKeyOverride = null)
    {
        if (!IsRecurring) throw new RequisitionException(RequisitionErrors.NotRecurring);
        if (Status is not RequisitionStatus.InPayment) throw new RequisitionException(RequisitionErrors.NotInPayment);
        var c = CompetencyFormat.Normalize(competency);
        if (!CompetencyFormat.IsValid(c)) throw new RequisitionException(RequisitionErrors.CycleCompetencyInvalid);
        if (CompetencyFormat.Compare(c, StartCompetency) <= 0) throw new RequisitionException(RequisitionErrors.CycleBeforeStart);
        if (EndCompetency is not null && CompetencyFormat.Compare(c, EndCompetency) > 0) throw new RequisitionException(RequisitionErrors.CycleAfterEnd);
        if (_cycles.Any(x => x.Competency == c)) throw new RequisitionException(RequisitionErrors.CycleCompetencyTaken);

        var cycle = new RequisitionCycle(Id, _cycles.Count + 1, c, amount, dueDate, method, boletoDigitableLine, actorId, at, pixKeyOverride);
        _cycles.Add(cycle);
        AppendTransition(RequisitionAction.AddRecurrenceCycle, actorId, at, $"fatura {c}");
        return cycle; // does NOT re-approve nor change status
    }

    /// <summary>Backs out a launched cycle whose Finance Payable was cancelled (AEL-877): the invoice will never
    /// be paid, so it must stop counting toward settlement (<see cref="AllCyclesSettled"/>) and its competency
    /// becomes free to relaunch. Matched by <paramref name="competency"/> (read off the cancelled Payable itself,
    /// so it works even before the link callback stamped <see cref="RequisitionCycle.PayableId"/>); a stale cancel
    /// for an OLD payable never removes a RELAUNCHED cycle already linked to a new one. Records an audit-only
    /// trail entry; if the recurrence is already terminated and everything else is covered, it settles on the
    /// spot. Returns false on no match (already backed out — at-least-once safe).</summary>
    public bool RemoveCycleForCancelledPayable(string competency, Guid payableId, DateTimeOffset at)
    {
        var c = CompetencyFormat.Normalize(competency);
        var cycle = _cycles.FirstOrDefault(x => x.Competency == c);
        if (cycle is null) return false;
        if (cycle.PayableId is not null && cycle.PayableId != payableId) return false; // relaunched — stale event
        _cycles.Remove(cycle);
        AppendTransition(RequisitionAction.RemoveRecurrenceCycle, SystemActor, at, $"fatura {cycle.Competency} cancelada no Financeiro");
        if (Status is RequisitionStatus.InPayment && EndCompetency is not null && AllCyclesSettled())
            MarkPaid(SystemActor, at);
        return true;
    }

    /// <summary>Terminates the recurrence: fixes the last competency (<see cref="EndCompetency"/>) so it can
    /// finalize (mirrors the legacy "close the Conta Fixa" action). Stays InPayment; if every cycle is already covered
    /// it settles right away (→ Paid). The end cannot precede the start nor the last launched cycle.</summary>
    public void TerminateRecurrence(string endCompetency, Guid actorId, DateTimeOffset at)
    {
        if (!IsRecurring) throw new RequisitionException(RequisitionErrors.NotRecurring);
        if (Status is not RequisitionStatus.InPayment) throw new RequisitionException(RequisitionErrors.NotInPayment);
        var end = CompetencyFormat.Normalize(endCompetency);
        if (!CompetencyFormat.IsValid(end)) throw new RequisitionException(RequisitionErrors.CycleCompetencyInvalid);
        if (CompetencyFormat.Compare(end, StartCompetency) < 0) throw new RequisitionException(RequisitionErrors.EndBeforeStart);
        var lastCycle = _cycles.Count == 0 ? StartCompetency : _cycles.Max(x => x.Competency)!;
        if (CompetencyFormat.Compare(end, lastCycle) < 0) throw new RequisitionException(RequisitionErrors.EndBeforeLastCycle);

        EndCompetency = end;
        AppendTransition(RequisitionAction.TerminateRecurrence, actorId, at, $"encerrada em {end}");
        if (AllCyclesSettled()) MarkPaid(SystemActor, at);
    }

    /// <summary>Cancels a requisition still in the pre-settlement flow. Blocked once real payments exist:
    /// money has moved (and Finance still holds a live Payable), so the payments must be reversed first.</summary>
    public void Cancel(Guid actorId, DateTimeOffset at, string? reason = null)
    {
        if (_payments.Any(p => !p.Reversed))
            throw new RequisitionException(RequisitionErrors.HasPayments);
        Transition(RequisitionStatus.Cancelled, RequisitionAction.Cancel, actorId, at, reason,
            RequisitionStatus.Draft, RequisitionStatus.Triage, RequisitionStatus.Approval,
            RequisitionStatus.Complement, RequisitionStatus.FinancialComplement, RequisitionStatus.InPayment);
    }

    /// <summary>Guards deletion: only a Draft may be hard-deleted (submitted requisitions are cancelled).</summary>
    public void EnsureDeletable()
    {
        if (Status != RequisitionStatus.Draft)
            throw new RequisitionException(RequisitionErrors.NotDeletable);
    }

    // ---- Competency window (base for the budget panel) -------------------------------------------

    /// <summary>Conta Fixa — a recurring requisition. Its <see cref="EstimateValue"/> is a PER-MONTH amount that
    /// repeats across the competency window, and once approved it stays perpetually
    /// <see cref="RequisitionStatus.InPayment"/> receiving each month's invoice: it never settles by payment
    /// (mirrors the legacy status reconciler), only by explicit termination once every cycle is covered.</summary>
    public bool IsRecurring => Type is RequisitionType.FixedExpense;

    /// <summary>Conta Fixa specifically (electricity/water/phone/internet) — a recurring expense of VARIABLE
    /// amount. Drives the budget rule exposed by <see cref="BypassesBudgetCeiling"/>.</summary>
    public bool IsFixedExpense => Type is RequisitionType.FixedExpense;

    /// <summary>Whether the Payable born from this requisition must pass tax analysis (withholdings) before
    /// scheduling: ServiceProvision and Conta Fixa carry withholdings, so they enter the blocking stage; Purchase
    /// (goods) and Project do not. Stamped on the Payable via RequireTaxAnalysis().</summary>
    public bool RequiresTaxAnalysis => Type is RequisitionType.ServiceProvision or RequisitionType.FixedExpense;

    /// <summary>Whether the budget ceiling is a soft cap for this requisition (AEL-881). A Conta Fixa varies
    /// month to month and cannot be capped, so it bypasses the HARD budget block both at approval
    /// (<see cref="Approve"/>) and at each invoice launch (<see cref="AddCycle"/> via the endpoint), while still
    /// contributing to the forecast for visibility. Every other type keeps the ceiling. This is
    /// the single source of truth for the exemption — endpoints must not re-derive it from the type.</summary>
    public bool BypassesBudgetCeiling => IsFixedExpense;

    /// <summary>A recurring requisition with an open window (no end competency) — the perpetual case: the payment
    /// plan materializes only the first cycle; the rest are launched later as each month's invoice arrives.</summary>
    private bool IsOpenEndedRecurring => IsRecurring && EndCompetency is null;

    /// <summary>Whether the value spreads over a competency window (vs. landing entirely on the start
    /// competency): Conta Fixa always span; otherwise a Monthly estimate spans and a Full
    /// estimate is single-competency.</summary>
    private bool SpansWindow => IsRecurring || EstimateType == EstimateType.Monthly;

    /// <summary>Whether this requisition contributes to the given competency (<c>"YYYY-MM"</c>).</summary>
    public bool AppliesToCompetency(string competency)
    {
        var c = CompetencyFormat.Normalize(competency);
        if (!CompetencyFormat.IsValid(c)) return false;
        return SpansWindow ? InWindow(c) : CompetencyFormat.Compare(c, StartCompetency) == 0;
    }

    /// <summary>Slice of the TOTAL (<see cref="EstimateValue"/>) attributed to a competency: the whole total
    /// on the start for a single-competency estimate; for a Monthly estimate over a CLOSED window the total
    /// split evenly with the residual cents on the LAST competency (slices sum to the total exactly — no lost
    /// cents); for an OPEN recurrence the per-month amount (no finite total to divide). Real payments override
    /// this later (Finance integration).</summary>
    public decimal EstimatedContributionFor(string competency)
    {
        var c = CompetencyFormat.Normalize(competency);
        if (!AppliesToCompetency(c)) return 0m;
        // Recurring (Conta Fixa): EstimateValue is the PER-MONTH amount — it repeats on every
        // competency of the window, whether open OR closed (never divided). Terminating a recurrence sets
        // EndCompetency but must NOT shrink the monthly forecast, so the recurring check comes first (R4).
        if (IsRecurring || !SpansWindow || EndCompetency is null) return EstimateValue;

        // Non-recurring Monthly over a CLOSED window: split the total evenly, residual on the last competency.
        var months = CompetencyFormat.CountMonths(StartCompetency, EndCompetency);
        if (months <= 1) return EstimateValue;
        var each = decimal.Round(EstimateValue / months, 2, MidpointRounding.AwayFromZero);
        var index = CompetencyFormat.CountMonths(StartCompetency, c) - 1; // 0-based position in the window
        return index >= months - 1 ? EstimateValue - each * (months - 1) : each;
    }

    /// <summary>Real (paid) contribution to a competency — the sum of non-reversed payments booked to it.
    /// The budget panel uses this instead of <see cref="EstimatedContributionFor"/> once payments exist.</summary>
    public decimal RealizedContributionFor(string competency)
    {
        var c = CompetencyFormat.Normalize(competency);
        return _payments.Where(p => !p.Reversed && p.Competency == c).Sum(p => p.Amount);
    }

    /// <summary>Total payment-plan cost = <see cref="EstimateValue"/> (the requisition's total). The
    /// installment split (<see cref="BuildPaymentSchedule"/>) divides this and puts the residual on the last
    /// installment, so the installments sum back to the total exactly.</summary>
    public decimal TotalCost() => EstimateValue;

    /// <summary>Builds the payment plan the generated Payable will use (AEL-793). Budgeting owns the cadence:
    /// due dates step from <see cref="FirstDueDate"/> by <see cref="PaymentFrequency"/> (Biweekly = every 15
    /// days; Monthly = +1 month; Semiannual = +6; Annual = +12). The amount is split evenly across the count
    /// with the remainder on the last installment. Open-ended recurring requisitions (Conta Fixa
    /// with no end) materialize a single installment for the first cycle — the rest are added later, mirroring
    /// the legacy app; otherwise the count is <see cref="PaymentInstallmentsCount"/> (min 1).</summary>
    public IReadOnlyList<(int Sequence, DateOnly DueDate, decimal Amount)> BuildPaymentSchedule()
    {
        // Authored plan (financial complement, AEL-844) is the source of truth once present — the requester
        // set the concrete due dates/amounts, so don't re-derive them from the plan summary.
        if (_installments.Count > 0)
            return _installments.OrderBy(i => i.Sequence).Select(i => (i.Sequence, i.DueDate, i.Amount)).ToList();

        var count = IsOpenEndedRecurring ? 1 : (PaymentInstallmentsCount is int c && c >= 2 ? c : 1);
        var total = IsOpenEndedRecurring ? EstimateValue : TotalCost();

        var each = decimal.Round(total / count, 2, MidpointRounding.AwayFromZero);
        var rows = new List<(int, DateOnly, decimal)>(count);
        var accumulated = 0m;
        for (var i = 0; i < count; i++)
        {
            var amount = i == count - 1 ? total - accumulated : each; // last absorbs the rounding residual
            accumulated += amount;
            rows.Add((i + 1, StepDueDate(i), amount));
        }
        return rows;
    }

    private DateOnly StepDueDate(int index) => PaymentFrequency switch
    {
        Budgeting.Domain.PaymentFrequency.Biweekly => FirstDueDate.AddDays(15 * index),
        Budgeting.Domain.PaymentFrequency.Semiannual => FirstDueDate.AddMonths(6 * index),
        Budgeting.Domain.PaymentFrequency.Annual => FirstDueDate.AddMonths(12 * index),
        _ => FirstDueDate.AddMonths(index), // Monthly or unspecified
    };

    // ---- Audit -----------------------------------------------------------------------------------

    public void MarkCreated(string userId, DateTimeOffset at) { CreatedBy = userId; CreatedAt = at; StatusChangedAt = at; MarkUpdated(userId, at); }
    public void MarkUpdated(string userId, DateTimeOffset at) { UpdatedBy = userId; UpdatedAt = at; }

    // ---- Helpers ---------------------------------------------------------------------------------

    private void EnsureFrom(params RequisitionStatus[] allowedFrom)
    {
        if (Array.IndexOf(allowedFrom, Status) < 0)
            throw new RequisitionException(RequisitionErrors.InvalidTransition);
    }

    private void MoveTo(RequisitionStatus target, RequisitionAction action, Guid actorId, DateTimeOffset at, string? reason)
    {
        var from = Status;
        Status = target;
        StatusChangedAt = at; // entered the new stage — SLA base
        // Head audit fields (UpdatedBy/At) are stamped by the AuditInterceptor on save; here we only append
        // the transition trail with the explicitly-supplied actor/time.
        _transitions.Add(new RequisitionTransition(Id, from, target, action, actorId, at, reason));
    }

    private void Transition(RequisitionStatus target, RequisitionAction action, Guid actorId, DateTimeOffset at, string? reason, params RequisitionStatus[] allowedFrom)
    {
        EnsureFrom(allowedFrom);
        MoveTo(target, action, actorId, at, reason);
    }

    /// <summary>Appends an audit-only entry to the trail (same from/to status, <see cref="StatusChangedAt"/>
    /// untouched) — used by the recurrence actions (AddCycle / TerminateRecurrence) that record history without
    /// advancing the lifecycle stage (so the stage SLA isn't reset every month).</summary>
    private void AppendTransition(RequisitionAction action, Guid actorId, DateTimeOffset at, string? reason) =>
        _transitions.Add(new RequisitionTransition(Id, Status, Status, action, actorId, at, reason));

    private void EnsureEditable()
    {
        if (Status is not (RequisitionStatus.Draft or RequisitionStatus.Complement or RequisitionStatus.FinancialComplement))
            throw new RequisitionException(RequisitionErrors.NotEditable);
    }

    private bool InWindow(string competency) =>
        CompetencyFormat.Compare(competency, StartCompetency) >= 0 &&
        (EndCompetency is null || CompetencyFormat.Compare(competency, EndCompetency) <= 0);

    private void SetDetails(
        RequisitionType type, string description, decimal estimateValue, EstimateType estimateType,
        string startCompetency, Guid vendorId, DateOnly firstDueDate, string? endCompetency, string? observation,
        PaymentFrequency? paymentFrequency, int? paymentInstallmentsCount)
    {
        if (string.IsNullOrWhiteSpace(description)) throw new ArgumentException("Description is required.", nameof(description));
        if (vendorId == Guid.Empty) throw new ArgumentException("VendorId is required.", nameof(vendorId));
        if (paymentInstallmentsCount is int count && count < 1)
            throw new ArgumentOutOfRangeException(nameof(paymentInstallmentsCount), "Installments count must be positive when provided.");
        // Recurring (Conta Fixa): the estimate is the PER-MONTH value repeating over the window, so the plan
        // must be Monthly and without an installments count — a count would make the typed value a TOTAL and
        // inflate the per-month forecast (the legacy app coerces these fields the same way). AEL-877.
        if (type is RequisitionType.FixedExpense
            && (estimateType != EstimateType.Monthly || paymentInstallmentsCount is not null))
            throw new RequisitionException(RequisitionErrors.RecurringPlanInvalid);

        var start = NormalizeCompetency(startCompetency, nameof(startCompetency));
        var end = NormalizeEndCompetency(endCompetency, start);

        // A front-loaded plan is vendor-specific: the boleto linha digitável / payment method
        // belong to the current vendor. Swapping the vendor invalidates it, so drop it here (the single choke
        // point for a vendor change) — the requester must re-author, and if they don't, ApproveTriage falls back
        // to the classic financial-complement stage. On create VendorId is still Guid.Empty, so this never fires.
        if (_installments.Count > 0 && VendorId != Guid.Empty && vendorId != VendorId)
        {
            _installments.Clear();
            IssueDate = null;
        }

        Type = type;
        Description = description.Trim();
        Observation = string.IsNullOrWhiteSpace(observation) ? null : observation.Trim();
        EstimateValue = NormalizeEstimate(estimateValue);
        EstimateType = estimateType;
        StartCompetency = start;
        EndCompetency = end;
        VendorId = vendorId;
        FirstDueDate = firstDueDate;
        PaymentFrequency = paymentFrequency;
        PaymentInstallmentsCount = paymentInstallmentsCount;
    }

    private static decimal NormalizeEstimate(decimal value)
    {
        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), "EstimateValue must be greater than zero.");
        return decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    }

    private static string NormalizeCompetency(string competency, string paramName)
    {
        var c = CompetencyFormat.Normalize(competency);
        if (!CompetencyFormat.IsValid(c))
            throw new ArgumentException($"Competency '{c}' is not a valid \"YYYY-MM\" value.", paramName);
        return c;
    }

    private static string? NormalizeEndCompetency(string? endCompetency, string start)
    {
        if (string.IsNullOrWhiteSpace(endCompetency)) return null;
        var end = NormalizeCompetency(endCompetency, nameof(endCompetency));
        if (CompetencyFormat.Compare(end, start) < 0)
            throw new ArgumentException($"EndCompetency '{end}' cannot precede StartCompetency '{start}'.", nameof(endCompetency));
        return end;
    }
}
