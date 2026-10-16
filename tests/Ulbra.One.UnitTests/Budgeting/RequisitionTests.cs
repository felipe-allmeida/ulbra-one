using Ulbra.One.Budgeting.Domain;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Domain.Errors;
using Ulbra.One.Common.Domain;
using Xunit;

namespace Ulbra.One.UnitTests.Budgeting;

public class RequisitionTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateTimeOffset At = DateTimeOffset.UnixEpoch;
    private static readonly DateOnly Due = new(2026, 7, 10);

    private static Requisition New(
        RequisitionType type = RequisitionType.Purchase,
        decimal estimateValue = 100m,
        EstimateType estimateType = EstimateType.Full,
        string start = "2026-07",
        string? end = null,
        string? observation = null,
        PaymentFrequency? frequency = null,
        int? installments = null,
        int code = 1,
        Guid? vendorId = null,
        DateOnly? firstDue = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), code, type, "Comprar cadeiras",
            estimateValue, estimateType, start, vendorId ?? Guid.NewGuid(), firstDue ?? new DateOnly(2026, 7, 10),
            end, observation, frequency, installments);

    private static void AssertInvalidTransition(Action act)
    {
        var ex = Assert.Throws<RequisitionException>(act);
        Assert.Equal(RequisitionErrors.InvalidTransition.Code, ex.Error.Code);
    }

    // ---- Construction ----------------------------------------------------------------------------

    [Fact]
    public void Constructor_sets_fields()
    {
        var company = Guid.NewGuid();
        var unit = Guid.NewGuid();
        var costCenter = Guid.NewGuid();
        var vendor = Guid.NewGuid();
        var r = new Requisition(company, unit, costCenter, 42, RequisitionType.ServiceProvision,
            "Serviço de limpeza", 1234.56m, EstimateType.Monthly, "2026-07", vendor, new DateOnly(2026, 7, 10),
            "2026-12", "obs", PaymentFrequency.Monthly, 6);

        Assert.Equal(company, r.CompanyId);
        Assert.Equal(unit, r.UnitId);
        Assert.Equal(costCenter, r.CostCenterId);
        Assert.Equal(42, r.Code);
        Assert.Equal(RequisitionType.ServiceProvision, r.Type);
        Assert.Equal("Serviço de limpeza", r.Description);
        Assert.Equal(1234.56m, r.EstimateValue);
        Assert.Equal(EstimateType.Monthly, r.EstimateType);
        Assert.Equal("2026-07", r.StartCompetency);
        Assert.Equal("2026-12", r.EndCompetency);
        Assert.Equal("obs", r.Observation);
        Assert.Equal(PaymentFrequency.Monthly, r.PaymentFrequency);
        Assert.Equal(6, r.PaymentInstallmentsCount);
        Assert.Equal(vendor, r.VendorId);
        Assert.Equal(new DateOnly(2026, 7, 10), r.FirstDueDate);
    }

    [Fact]
    public void New_requisition_starts_as_draft() => Assert.Equal(RequisitionStatus.Draft, New().Status);

    [Fact]
    public void Constructor_trims_description_and_observation()
    {
        var r = new Requisition(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, RequisitionType.Purchase,
            "  desc  ", 10m, EstimateType.Full, "2026-07", Guid.NewGuid(), new DateOnly(2026, 7, 10), null, "  obs  ", null, null);
        Assert.Equal("desc", r.Description);
        Assert.Equal("obs", r.Observation);
    }

    [Fact]
    public void Constructor_null_or_blank_observation_becomes_null() =>
        Assert.Null(New(observation: "   ").Observation);

    [Fact]
    public void Constructor_rounds_estimate_to_two_decimals() =>
        Assert.Equal(100.13m, New(estimateValue: 100.125m).EstimateValue); // AwayFromZero

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_rejects_non_positive_estimate(decimal value) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => New(estimateValue: value));

    [Theory]
    [InlineData("2026-13")]
    [InlineData("202607")]
    [InlineData("")]
    public void Constructor_rejects_invalid_start_competency(string start) =>
        Assert.Throws<ArgumentException>(() => New(start: start));

    [Fact]
    public void Constructor_rejects_end_before_start() =>
        Assert.Throws<ArgumentException>(() => New(start: "2026-07", end: "2026-06"));

    [Fact]
    public void Constructor_allows_equal_start_and_end() =>
        Assert.Equal("2026-07", New(start: "2026-07", end: "2026-07").EndCompetency);

    [Fact]
    public void Constructor_rejects_code_below_one() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => New(code: 0));

    [Fact]
    public void Constructor_rejects_non_positive_installments() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => New(installments: 0));

    [Fact]
    public void Constructor_rejects_empty_ids()
    {
        Assert.Throws<ArgumentException>(() => new Requisition(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), 1, RequisitionType.Purchase, "d", 1m, EstimateType.Full, "2026-07", Guid.NewGuid(), new DateOnly(2026, 7, 10)));
        Assert.Throws<ArgumentException>(() => new Requisition(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), 1, RequisitionType.Purchase, "d", 1m, EstimateType.Full, "2026-07", Guid.NewGuid(), new DateOnly(2026, 7, 10)));
        Assert.Throws<ArgumentException>(() => new Requisition(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, 1, RequisitionType.Purchase, "d", 1m, EstimateType.Full, "2026-07", Guid.NewGuid(), new DateOnly(2026, 7, 10)));
    }

    // ---- State machine: happy paths --------------------------------------------------------------

    [Fact]
    public void Full_lifecycle_draft_to_finalized()
    {
        var r = New();
        Submit(r); Assert.Equal(RequisitionStatus.Triage, r.Status);
        r.ApproveTriage(Actor, At); Assert.Equal(RequisitionStatus.FinancialComplement, r.Status);
        SubmitFC(r, 100m); Assert.Equal(RequisitionStatus.Approval, r.Status);
        r.Approve(Actor, At); Assert.Equal(RequisitionStatus.InPayment, r.Status);
        r.MarkAwaitingFiscalDocument(Actor, At); Assert.Equal(RequisitionStatus.AwaitingFiscalDocument, r.Status);
        r.MarkPaid(Actor, At); Assert.Equal(RequisitionStatus.Paid, r.Status);
        r.MarkFinalized(Actor, At); Assert.Equal(RequisitionStatus.Finalized, r.Status);
    }

    [Fact]
    public void MarkPaid_directly_from_in_payment()
    {
        var r = Drive(RequisitionStatus.InPayment);
        r.MarkPaid(Actor, At);
        Assert.Equal(RequisitionStatus.Paid, r.Status);
    }

    [Fact]
    public void Complement_round_trip_from_triage()
    {
        var r = New();
        Submit(r);
        r.ReturnForComplement(Actor, At); Assert.Equal(RequisitionStatus.Complement, r.Status);
        r.Resubmit(Actor, At); Assert.Equal(RequisitionStatus.Triage, r.Status);
    }

    [Fact]
    public void Return_for_complement_from_approval()
    {
        var r = New();
        Submit(r); r.ApproveTriage(Actor, At); SubmitFC(r, 100m);
        Assert.Equal(RequisitionStatus.Approval, r.Status);
        r.ReturnForComplement(Actor, At);
        Assert.Equal(RequisitionStatus.Complement, r.Status);
    }

    // ---- State machine: Finance rejection returns the requisition (AEL-935) ----------------------

    [Fact]
    public void ReturnFromPayment_to_requester_moves_to_complement_with_reason()
    {
        var r = Drive(RequisitionStatus.InPayment);
        r.ReturnFromPayment(RequisitionReturnTarget.Requester, Actor, At, "NF divergente");
        Assert.Equal(RequisitionStatus.Complement, r.Status);
        Assert.Equal(RequisitionAction.ReturnFromPayment, r.Transitions[^1].Action);
        Assert.Equal("NF divergente", r.Transitions[^1].Reason);
    }

    [Fact]
    public void ReturnFromPayment_to_controllership_moves_to_triage()
    {
        var r = Drive(RequisitionStatus.InPayment);
        r.ReturnFromPayment(RequisitionReturnTarget.Controllership, Actor, At, "reclassificar");
        Assert.Equal(RequisitionStatus.Triage, r.Status);
    }

    [Fact]
    public void ReturnFromPayment_is_allowed_from_awaiting_fiscal_document()
    {
        var r = Drive(RequisitionStatus.AwaitingFiscalDocument);
        r.ReturnFromPayment(RequisitionReturnTarget.Requester, Actor, At, null);
        Assert.Equal(RequisitionStatus.Complement, r.Status);
    }

    [Fact]
    public void ReturnFromPayment_from_invalid_state_throws()
    {
        var r = Drive(RequisitionStatus.Approval);
        AssertInvalidTransition(() => r.ReturnFromPayment(RequisitionReturnTarget.Requester, Actor, At, "x"));
    }

    // ---- State machine: financial complement (AEL-840) -------------------------------------------

    [Fact]
    public void ApproveTriage_moves_to_financial_complement_and_snapshots_value_and_vendor()
    {
        var vendor = Guid.NewGuid();
        var r = New(estimateValue: 500m, vendorId: vendor);
        Submit(r);
        r.ApproveTriage(Actor, At);
        Assert.Equal(RequisitionStatus.FinancialComplement, r.Status);
        Assert.Equal(500m, r.TriageApprovedValue);
        Assert.Equal(vendor, r.TriageApprovedVendorId);
    }

    [Fact]
    public void SubmitFinancialComplement_same_value_and_vendor_moves_to_approval()
    {
        var r = New(estimateValue: 500m);
        Submit(r);
        r.ApproveTriage(Actor, At);
        SubmitFC(r, 500m);
        Assert.Equal(RequisitionStatus.Approval, r.Status);
    }

    [Fact]
    public void SubmitFinancialComplement_lowered_value_still_moves_to_approval()
    {
        var r = New(estimateValue: 500m);
        Submit(r);
        r.ApproveTriage(Actor, At);
        SubmitFC(r, 400m); // Σ parcelas below the triage-approved amount
        Assert.Equal(RequisitionStatus.Approval, r.Status);
        Assert.Equal(400m, r.EstimateValue); // programado becomes the requisition value
    }

    [Fact]
    public void SubmitFinancialComplement_raised_value_returns_to_triage()
    {
        var r = New(estimateValue: 500m);
        Submit(r);
        r.ApproveTriage(Actor, At);
        SubmitFC(r, 900m); // Σ parcelas above the triage-approved amount
        Assert.Equal(RequisitionStatus.Triage, r.Status);
    }

    [Fact]
    public void SubmitFinancialComplement_changed_vendor_returns_to_triage()
    {
        var r = New(estimateValue: 500m);
        Submit(r);
        r.ApproveTriage(Actor, At);
        r.Update(RequisitionType.Purchase, "Comprar cadeiras", 500m, EstimateType.Full, "2026-07",
            Guid.NewGuid(), r.FirstDueDate, null, null, null, null);
        SubmitFC(r, 500m);
        Assert.Equal(RequisitionStatus.Triage, r.Status);
    }

    [Fact]
    public void SubmitFinancialComplement_persists_authored_installments_and_issue_date()
    {
        var r = New(estimateValue: 500m);
        Submit(r);
        r.ApproveTriage(Actor, At);
        var issue = new DateOnly(2026, 7, 1);
        r.SubmitFinancialComplement(Actor, At, issue,
        [
            (new DateOnly(2026, 7, 10), 200m, PaymentMethod.Pix, null, null),
            (new DateOnly(2026, 8, 10), 300m, PaymentMethod.Boleto, null, null),
        ]);
        Assert.Equal(RequisitionStatus.Approval, r.Status);
        Assert.Equal(issue, r.IssueDate);
        Assert.Equal(500m, r.EstimateValue); // programado = Σ parcelas
        Assert.Collection(r.Installments,
            i => { Assert.Equal(1, i.Sequence); Assert.Equal(200m, i.Amount); Assert.Equal(PaymentMethod.Pix, i.PaymentMethod); },
            i => { Assert.Equal(2, i.Sequence); Assert.Equal(300m, i.Amount); Assert.Equal(PaymentMethod.Boleto, i.PaymentMethod); });
    }

    [Fact]
    public void SubmitFinancialComplement_persists_boleto_digitable_line_per_installment()
    {
        var r = New(estimateValue: 500m);
        Submit(r);
        r.ApproveTriage(Actor, At);
        r.SubmitFinancialComplement(Actor, At, new DateOnly(2026, 7, 1),
        [
            (new DateOnly(2026, 7, 10), 200m, PaymentMethod.Pix, "   ", null), // blank normalizes to null
            (new DateOnly(2026, 8, 10), 300m, PaymentMethod.Boleto, " 34191790010104351004791020150008291070026000 ", null),
        ]);
        Assert.Collection(r.Installments,
            i => Assert.Null(i.BoletoDigitableLine),
            i => Assert.Equal("34191790010104351004791020150008291070026000", i.BoletoDigitableLine)); // trimmed
    }

    [Fact]
    public void SubmitFinancialComplement_authored_installments_drive_build_schedule()
    {
        var r = New(estimateValue: 500m);
        Submit(r);
        r.ApproveTriage(Actor, At);
        r.SubmitFinancialComplement(Actor, At, new DateOnly(2026, 7, 1),
        [
            (new DateOnly(2026, 7, 10), 200m, PaymentMethod.Pix, null, null),
            (new DateOnly(2026, 8, 10), 300m, PaymentMethod.Pix, null, null),
        ]);
        var plan = r.BuildPaymentSchedule();
        Assert.Equal(2, plan.Count);
        Assert.Equal(new DateOnly(2026, 8, 10), plan[1].DueDate);
        Assert.Equal(500m, plan.Sum(p => p.Amount));
    }

    [Fact]
    public void SubmitFinancialComplement_requires_at_least_one_installment()
    {
        var r = New(estimateValue: 500m);
        Submit(r);
        r.ApproveTriage(Actor, At);
        var ex = Assert.Throws<RequisitionException>(() =>
            r.SubmitFinancialComplement(Actor, At, new DateOnly(2026, 7, 1), []));
        Assert.Equal(RequisitionErrors.InstallmentsRequired.Code, ex.Error.Code);
    }

    [Fact]
    public void SubmitFinancialComplement_missing_issue_date_is_rejected()
    {
        var r = New(estimateValue: 500m);
        Submit(r);
        r.ApproveTriage(Actor, At);
        var ex = Assert.Throws<RequisitionException>(() =>
            r.SubmitFinancialComplement(Actor, At, null, [(new DateOnly(2026, 7, 10), 500m, PaymentMethod.Pix, null, null)]));
        Assert.Equal(RequisitionErrors.IssueDateRequired.Code, ex.Error.Code);
        Assert.Equal(RequisitionStatus.FinancialComplement, r.Status); // unchanged
    }

    [Fact]
    public void SubmitFinancialComplement_issue_date_after_earliest_due_is_rejected()
    {
        var r = New(estimateValue: 500m);
        Submit(r);
        r.ApproveTriage(Actor, At);
        var ex = Assert.Throws<RequisitionException>(() =>
            r.SubmitFinancialComplement(Actor, At, new DateOnly(2026, 8, 1),
                [(new DateOnly(2026, 7, 10), 300m, PaymentMethod.Pix, null, null), (new DateOnly(2026, 9, 10), 200m, PaymentMethod.Boleto, null, null)]));
        Assert.Equal(RequisitionErrors.IssueDateAfterFirstDueDate.Code, ex.Error.Code);
        Assert.Equal(RequisitionStatus.FinancialComplement, r.Status); // unchanged
    }

    [Fact]
    public void SubmitFinancialComplement_issue_date_equal_earliest_due_is_accepted()
    {
        var r = New(estimateValue: 500m);
        Submit(r);
        r.ApproveTriage(Actor, At);
        r.SubmitFinancialComplement(Actor, At, new DateOnly(2026, 7, 10),
            [(new DateOnly(2026, 7, 10), 500m, PaymentMethod.Pix, null, null)]);
        Assert.Equal(RequisitionStatus.Approval, r.Status);
        Assert.Equal(new DateOnly(2026, 7, 10), r.IssueDate);
    }

    [Fact]
    public void Requisition_is_editable_in_financial_complement()
    {
        var r = New(estimateValue: 500m);
        Submit(r);
        r.ApproveTriage(Actor, At);
        r.Update(RequisitionType.Purchase, "Nova descricao", 600m, EstimateType.Full, "2026-07",
            r.VendorId, r.FirstDueDate, null, null, null, null);
        Assert.Equal(600m, r.EstimateValue);
    }

    [Fact]
    public void Cancel_from_financial_complement()
    {
        var r = New();
        Submit(r);
        r.ApproveTriage(Actor, At);
        r.Cancel(Actor, At);
        Assert.Equal(RequisitionStatus.Cancelled, r.Status);
    }

    [Fact]
    public void ApproveTriage_only_from_triage()
    {
        var r = New();
        AssertInvalidTransition(() => r.ApproveTriage(Actor, At));
    }

    [Fact]
    public void SubmitFinancialComplement_only_from_financial_complement()
    {
        var r = New();
        Submit(r);
        AssertInvalidTransition(() => SubmitFC(r, 100m));
    }

    // ---- State machine: front-load financial plan (Onda 2) ---------------------------------------

    [Fact]
    public void SetFinancialPlan_stores_plan_without_transition()
    {
        var r = New(estimateValue: 100m);
        SetPlan(r, 250m);
        Assert.Equal(RequisitionStatus.Draft, r.Status); // no transition
        Assert.Single(r.Installments);
        Assert.Equal(Due, r.IssueDate);
        Assert.Equal(250m, r.EstimateValue); // Σ parcelas overwrites the typed estimate
    }

    [Fact]
    public void SetFinancialPlan_allowed_in_complement_and_financial_complement()
    {
        var complement = Drive(RequisitionStatus.Complement);
        SetPlan(complement, 100m);
        Assert.Single(complement.Installments);
        Assert.Equal(RequisitionStatus.Complement, complement.Status);

        var financial = Drive(RequisitionStatus.FinancialComplement);
        SetPlan(financial, 100m);
        Assert.Single(financial.Installments);
        Assert.Equal(RequisitionStatus.FinancialComplement, financial.Status);
    }

    [Fact]
    public void SetFinancialPlan_rejected_when_not_editable()
    {
        var triage = Drive(RequisitionStatus.Triage);
        var ex = Assert.Throws<RequisitionException>(() => SetPlan(triage, 100m));
        Assert.Equal(RequisitionErrors.NotEditable.Code, ex.Error.Code);

        var approval = Drive(RequisitionStatus.Approval);
        Assert.Equal(RequisitionErrors.NotEditable.Code,
            Assert.Throws<RequisitionException>(() => SetPlan(approval, 100m)).Error.Code);
    }

    [Fact]
    public void SetFinancialPlan_recurring_requires_single_installment()
    {
        var r = New(type: RequisitionType.FixedExpense, estimateType: EstimateType.Monthly);
        var ex = Assert.Throws<RequisitionException>(() => r.SetFinancialPlan(Due,
        [
            (new DateOnly(2026, 7, 10), 100m, PaymentMethod.Pix, null, null),
            (new DateOnly(2026, 8, 10), 100m, PaymentMethod.Pix, null, null),
        ]));
        Assert.Equal(RequisitionErrors.RecurringSingleInstallment.Code, ex.Error.Code);
    }

    [Fact]
    public void SetFinancialPlan_requires_issue_date()
    {
        var r = New();
        var ex = Assert.Throws<RequisitionException>(() =>
            r.SetFinancialPlan(null, [(Due, 100m, PaymentMethod.Pix, null, null)]));
        Assert.Equal(RequisitionErrors.IssueDateRequired.Code, ex.Error.Code);
    }

    [Fact]
    public void SetFinancialPlan_requires_at_least_one_installment()
    {
        var r = New();
        var ex = Assert.Throws<RequisitionException>(() => r.SetFinancialPlan(Due, []));
        Assert.Equal(RequisitionErrors.InstallmentsRequired.Code, ex.Error.Code);
    }

    [Fact]
    public void SetFinancialPlan_issue_date_after_earliest_due_is_rejected()
    {
        var r = New();
        var ex = Assert.Throws<RequisitionException>(() =>
            r.SetFinancialPlan(new DateOnly(2026, 8, 1), [(new DateOnly(2026, 7, 10), 100m, PaymentMethod.Pix, null, null)]));
        Assert.Equal(RequisitionErrors.IssueDateAfterFirstDueDate.Code, ex.Error.Code);
    }

    [Fact]
    public void ClearFinancialPlan_removes_plan()
    {
        var r = New();
        SetPlan(r, 100m);
        r.ClearFinancialPlan();
        Assert.Empty(r.Installments);
        Assert.Null(r.IssueDate);
    }

    [Fact]
    public void ApproveTriage_with_complete_plan_goes_straight_to_approval()
    {
        var vendor = Guid.NewGuid();
        var r = New(estimateValue: 500m, vendorId: vendor);
        SetPlan(r, 500m);
        Submit(r);
        r.ApproveTriage(Actor, At);
        Assert.Equal(RequisitionStatus.Approval, r.Status); // skipped FinancialComplement
        Assert.Equal(500m, r.TriageApprovedValue);
        Assert.Equal(vendor, r.TriageApprovedVendorId);
    }

    // ---- Boleto linha digitável guard on Submit (Draft→Triage) — 47-digit CNAB convênio -----

    // A draft can hold an incomplete boleto line (the AI reader can drop a digit); it stays a Draft. The 47/48
    // rule fires only on Draft→Triage.
    [Fact]
    public void SetFinancialPlan_allows_an_incomplete_convenio_boleto_line_on_a_draft()
    {
        var r = New(estimateValue: 789.18m);
        r.SetFinancialPlan(Due, [(Due, 789.18m, PaymentMethod.Boleto, "82650000007889180798000002026177663112901001043", null)]); // 47 digits, lead 8
        Assert.Single(r.Installments);
        Assert.Equal(RequisitionStatus.Draft, r.Status);
    }

    [Fact]
    public void Submit_with_a_47digit_convenio_boleto_is_rejected()
    {
        var r = New(estimateValue: 789.18m);
        r.SetFinancialPlan(Due, [(Due, 789.18m, PaymentMethod.Boleto, "82650000007889180798000002026177663112901001043", null)]);
        var ex = Assert.Throws<RequisitionException>(() => r.Submit(Actor, At));
        Assert.Equal(RequisitionErrors.BoletoDigitableLineInvalid.Code, ex.Error.Code);
        Assert.Equal(RequisitionStatus.Draft, r.Status); // not transitioned
    }

    [Fact]
    public void Submit_with_a_valid_48digit_convenio_boleto_succeeds()
    {
        var r = New(estimateValue: 300m);
        r.SetFinancialPlan(Due, [(Due, 300m, PaymentMethod.Boleto, "846700000017599200241114202410000000000000000000", null)]);
        r.Submit(Actor, At);
        Assert.Equal(RequisitionStatus.Triage, r.Status);
    }

    [Fact]
    public void SetDetails_vendor_change_clears_front_loaded_plan()
    {
        var vendor = Guid.NewGuid();
        var r = New(estimateValue: 300m, vendorId: vendor);
        r.SetFinancialPlan(Due, [(Due, 300m, PaymentMethod.Boleto, "34191790010104351004791020150008291070026000", null)]);
        Assert.Single(r.Installments);

        // Same vendor → plan kept.
        r.Update(RequisitionType.Purchase, "Comprar cadeiras", 300m, EstimateType.Full, "2026-07",
            vendor, r.FirstDueDate, null, null, null, null);
        Assert.Single(r.Installments);

        // Vendor swap → plan dropped (linha digitável belonged to the old vendor).
        r.Update(RequisitionType.Purchase, "Comprar cadeiras", 300m, EstimateType.Full, "2026-07",
            Guid.NewGuid(), r.FirstDueDate, null, null, null, null);
        Assert.Empty(r.Installments);
        Assert.Null(r.IssueDate);
    }

    [Fact]
    public void ReturnToFinancialComplement_from_approval()
    {
        var r = Drive(RequisitionStatus.Approval);
        r.ReturnToFinancialComplement(Actor, At, "ajustar parcelas");
        Assert.Equal(RequisitionStatus.FinancialComplement, r.Status);
        Assert.Equal("ajustar parcelas", r.Transitions[^1].Reason);
        Assert.Equal(RequisitionAction.ReturnToFinancialComplement, r.Transitions[^1].Action);
    }

    [Fact]
    public void ReturnToFinancialComplement_only_from_approval()
    {
        AssertInvalidTransition(() => New().ReturnToFinancialComplement(Actor, At));
        AssertInvalidTransition(() => Drive(RequisitionStatus.Triage).ReturnToFinancialComplement(Actor, At));
        AssertInvalidTransition(() => Drive(RequisitionStatus.FinancialComplement).ReturnToFinancialComplement(Actor, At));
    }

    [Fact]
    public void History_front_loaded_skips_financial_complement()
    {
        var r = New(estimateValue: 500m);
        SetPlan(r, 500m);
        Submit(r);
        r.ApproveTriage(Actor, At);
        Assert.Equal(RequisitionStatus.Approval, r.Status);
        r.Approve(Actor, At);
        Assert.Equal(
            new[] { RequisitionAction.Submit, RequisitionAction.ApproveTriage, RequisitionAction.Approve },
            r.Transitions.Select(t => t.Action).ToArray());
    }

    [Theory]
    [InlineData(RequisitionStatus.Draft)]
    [InlineData(RequisitionStatus.Triage)]
    [InlineData(RequisitionStatus.Approval)]
    [InlineData(RequisitionStatus.Complement)]
    [InlineData(RequisitionStatus.InPayment)]
    public void Cancel_allowed_from_pre_settlement_states(RequisitionStatus from)
    {
        var r = Drive(from);
        r.Cancel(Actor, At);
        Assert.Equal(RequisitionStatus.Cancelled, r.Status);
    }

    [Fact]
    public void Cancel_blocked_when_non_reversed_payments_exist()
    {
        var r = Drive(RequisitionStatus.InPayment, estimateValue: 1000m);
        r.RegisterPayment(Guid.NewGuid(), Guid.NewGuid(), 400m, "2026-07", At); // partial → stays InPayment
        var ex = Assert.Throws<RequisitionException>(() => r.Cancel(Actor, At));
        Assert.Equal(ErrorCodes.Requisition.HasPayments, ex.Error.Code);
        Assert.Equal(RequisitionStatus.InPayment, r.Status); // unchanged
    }

    [Fact]
    public void Cancel_allowed_after_all_payments_reversed()
    {
        var r = Drive(RequisitionStatus.InPayment, estimateValue: 1000m);
        var paymentId = Guid.NewGuid();
        r.RegisterPayment(paymentId, Guid.NewGuid(), 400m, "2026-07", At);
        r.ReversePayment(paymentId, At);
        r.Cancel(Actor, At);
        Assert.Equal(RequisitionStatus.Cancelled, r.Status);
    }

    // ---- State machine: invalid transitions ------------------------------------------------------

    [Fact]
    public void Approve_from_draft_is_invalid() => AssertInvalidTransition(() => New().Approve(Actor, At));

    [Fact]
    public void Submit_from_triage_is_invalid()
    {
        var r = New();
        Submit(r);
        AssertInvalidTransition(() => Submit(r));
    }

    [Fact]
    public void Finalize_from_draft_is_invalid() => AssertInvalidTransition(() => New().MarkFinalized(Actor, At));

    [Fact]
    public void Resubmit_from_draft_is_invalid() => AssertInvalidTransition(() => New().Resubmit(Actor, At));

    [Fact]
    public void Cancel_from_paid_is_invalid()
    {
        var r = Drive(RequisitionStatus.Paid);
        AssertInvalidTransition(() => r.Cancel(Actor, At));
    }

    [Fact]
    public void No_transition_out_of_finalized()
    {
        var r = Drive(RequisitionStatus.Finalized);
        AssertInvalidTransition(() => r.Cancel(Actor, At));
        AssertInvalidTransition(() => Submit(r));
    }

    // ---- Budget ceiling guard (AEL-830; hard block, no override — AEL-841) -----------------------

    [Fact]
    public void Approve_blocked_when_budget_exceeded()
    {
        var r = Drive(RequisitionStatus.Approval, estimateValue: 1000m);
        var ex = Assert.Throws<RequisitionException>(() => r.Approve(Actor, At, candidateExceeds: true));
        Assert.Equal(RequisitionErrors.BudgetExceeded.Code, ex.Error.Code);
        Assert.Equal(RequisitionStatus.Approval, r.Status); // unchanged — no override, hard block
    }

    [Fact]
    public void Approve_passes_when_budget_not_exceeded()
    {
        var r = Drive(RequisitionStatus.Approval, estimateValue: 1000m);
        r.Approve(Actor, At, candidateExceeds: false);
        Assert.Equal(RequisitionStatus.InPayment, r.Status);
        Assert.Null(r.Transitions[^1].Reason);
    }

    [Fact]
    public void Approve_defaults_to_no_budget_check_and_passes()
    {
        var r = Drive(RequisitionStatus.Approval, estimateValue: 1_000_000m);
        r.Approve(Actor, At); // default BudgetCheck = not exceeded; no alçada anymore
        Assert.Equal(RequisitionStatus.InPayment, r.Status);
    }

    // ---- Transition history ----------------------------------------------------------------------

    [Fact]
    public void Transition_records_history_entry()
    {
        var r = New();
        Submit(r);
        var t = Assert.Single(r.Transitions);
        Assert.Equal(RequisitionStatus.Draft, t.FromStatus);
        Assert.Equal(RequisitionStatus.Triage, t.ToStatus);
        Assert.Equal(RequisitionAction.Submit, t.Action);
        Assert.Equal(Actor, t.ActorId);
        Assert.Equal(At, t.OccurredAt);
        Assert.Null(t.Reason);
    }

    [Fact]
    public void Return_and_cancel_record_reason()
    {
        var r = New();
        Submit(r);
        r.ReturnForComplement(Actor, At, "faltou orçamento");
        Assert.Equal("faltou orçamento", r.Transitions[^1].Reason);

        r.Resubmit(Actor, At);
        r.Cancel(Actor, At, "duplicada");
        Assert.Equal("duplicada", r.Transitions[^1].Reason);
    }

    [Fact]
    public void History_accumulates_in_order()
    {
        var r = New();
        Submit(r);
        r.ApproveTriage(Actor, At);
        SubmitFC(r, 100m);
        r.Approve(Actor, At);
        Assert.Equal(
            new[] { RequisitionAction.Submit, RequisitionAction.ApproveTriage, RequisitionAction.SubmitFinancialComplement, RequisitionAction.Approve },
            r.Transitions.Select(t => t.Action).ToArray());
    }

    [Fact]
    public void Failed_transition_records_no_history_and_leaves_status()
    {
        var r = New(); // Draft, no transitions yet
        AssertInvalidTransition(() => r.Approve(Actor, At));
        Assert.Empty(r.Transitions);
        Assert.Equal(RequisitionStatus.Draft, r.Status);
    }

    [Fact]
    public void Blocked_approval_records_no_history()
    {
        // 3 transitions to reach Approval: Submit, ApproveTriage, SubmitFinancialComplement.
        var r = Drive(RequisitionStatus.Approval, estimateValue: 1000m);
        Assert.Throws<RequisitionException>(() => r.Approve(Actor, At, candidateExceeds: true));
        Assert.Equal(3, r.Transitions.Count);
    }

    // ---- Update ----------------------------------------------------------------------------------

    [Fact]
    public void Update_changes_editable_fields_in_draft()
    {
        var r = New();
        // Non-recurring type: FixedExpense/Recorrência reject an installments count (RecurringPlanInvalid).
        r.Update(RequisitionType.ServiceProvision, "novo", 250m, EstimateType.Monthly, "2026-08", Guid.NewGuid(), new DateOnly(2026, 8, 5), "2026-10", "nota", PaymentFrequency.Biweekly, 4);
        Assert.Equal(RequisitionType.ServiceProvision, r.Type);
        Assert.Equal("novo", r.Description);
        Assert.Equal(250m, r.EstimateValue);
        Assert.Equal(EstimateType.Monthly, r.EstimateType);
        Assert.Equal("2026-08", r.StartCompetency);
        Assert.Equal("2026-10", r.EndCompetency);
        Assert.Equal("nota", r.Observation);
        Assert.Equal(PaymentFrequency.Biweekly, r.PaymentFrequency);
        Assert.Equal(4, r.PaymentInstallmentsCount);
    }

    [Fact]
    public void Update_allowed_in_complement()
    {
        var r = New();
        Submit(r); r.ReturnForComplement(Actor, At);
        r.Update(RequisitionType.Purchase, "ajustado", 99m, EstimateType.Full, "2026-07", Guid.NewGuid(), new DateOnly(2026, 7, 10), null, null, null, null);
        Assert.Equal("ajustado", r.Description);
    }

    [Fact]
    public void Update_after_submit_throws_not_editable()
    {
        var r = New();
        Submit(r);
        var ex = Assert.Throws<RequisitionException>(() =>
            r.Update(RequisitionType.Purchase, "x", 1m, EstimateType.Full, "2026-07", Guid.NewGuid(), new DateOnly(2026, 7, 10), null, null, null, null));
        Assert.Equal(RequisitionErrors.NotEditable.Code, ex.Error.Code);
    }

    [Fact]
    public void Update_validates_estimate()
    {
        var r = New();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            r.Update(RequisitionType.Purchase, "x", 0m, EstimateType.Full, "2026-07", Guid.NewGuid(), new DateOnly(2026, 7, 10), null, null, null, null));
    }

    // ---- Delete guard ----------------------------------------------------------------------------

    [Fact]
    public void EnsureDeletable_passes_for_draft() => New().EnsureDeletable(); // does not throw

    [Fact]
    public void EnsureDeletable_throws_after_submit()
    {
        var r = New();
        Submit(r);
        var ex = Assert.Throws<RequisitionException>(() => r.EnsureDeletable());
        Assert.Equal(RequisitionErrors.NotDeletable.Code, ex.Error.Code);
    }

    // ---- Competency window -----------------------------------------------------------------------

    [Fact]
    public void Full_applies_only_to_start_competency()
    {
        var r = New(type: RequisitionType.Purchase, estimateType: EstimateType.Full, start: "2026-07", end: "2026-12");
        Assert.True(r.AppliesToCompetency("2026-07"));
        Assert.False(r.AppliesToCompetency("2026-08"));
    }

    [Fact]
    public void Monthly_applies_across_bounded_range()
    {
        var r = New(type: RequisitionType.Purchase, estimateType: EstimateType.Monthly, start: "2026-07", end: "2026-09");
        Assert.False(r.AppliesToCompetency("2026-06"));
        Assert.True(r.AppliesToCompetency("2026-07"));
        Assert.True(r.AppliesToCompetency("2026-09"));
        Assert.False(r.AppliesToCompetency("2026-10"));
    }

    [Fact]
    public void Monthly_open_ended_applies_from_start_onward()
    {
        var r = New(type: RequisitionType.Purchase, estimateType: EstimateType.Monthly, start: "2026-07", end: null);
        Assert.True(r.AppliesToCompetency("2026-07"));
        Assert.True(r.AppliesToCompetency("2030-01"));
        Assert.False(r.AppliesToCompetency("2026-06"));
    }

    [Fact]
    public void FixedExpense_spans_its_closed_window()
    {
        // Conta Fixa é sempre Mensal (RecurringPlanInvalid guarda o plano) e abrange toda a janela fechada
        // — ex.: uma recorrência já encerrada.
        var r = New(type: RequisitionType.FixedExpense, estimateType: EstimateType.Monthly, start: "2026-07", end: "2026-09");
        Assert.True(r.AppliesToCompetency("2026-07"));
        Assert.True(r.AppliesToCompetency("2026-08"));
        Assert.True(r.AppliesToCompetency("2026-09"));
        Assert.False(r.AppliesToCompetency("2026-10"));
    }

    [Fact]
    public void FixedExpense_open_ended_applies_from_start_onward()
    {
        var r = New(type: RequisitionType.FixedExpense, estimateType: EstimateType.Monthly, start: "2026-07", end: null);
        Assert.True(r.AppliesToCompetency("2026-07"));
        Assert.True(r.AppliesToCompetency("2099-12"));
    }

    [Fact]
    public void IsRecurring_and_IsFixedExpense_hold_only_for_Conta_Fixa()
    {
        // Conta Fixa é o único tipo recorrente (dispara as 3 regras AEL-881: sem teto, unicidade, NF por fatura).
        var fixedExpense = New(type: RequisitionType.FixedExpense, estimateType: EstimateType.Monthly);
        Assert.True(fixedExpense.IsFixedExpense);
        Assert.True(fixedExpense.IsRecurring);
        Assert.True(fixedExpense.BypassesBudgetCeiling);

        var purchase = New(type: RequisitionType.Purchase, estimateType: EstimateType.Full);
        Assert.False(purchase.IsFixedExpense);
        Assert.False(purchase.IsRecurring);
        Assert.False(purchase.BypassesBudgetCeiling);
    }

    [Fact]
    public void AppliesToCompetency_false_for_invalid_input() =>
        Assert.False(New().AppliesToCompetency("nope"));

    [Fact]
    public void EstimatedContribution_is_value_when_applies_else_zero()
    {
        var r = New(estimateValue: 500m, type: RequisitionType.Purchase, estimateType: EstimateType.Full, start: "2026-07");
        Assert.Equal(500m, r.EstimatedContributionFor("2026-07"));
        Assert.Equal(0m, r.EstimatedContributionFor("2026-08"));
    }

    [Fact]
    public void Monthly_contribution_splits_the_total_across_the_window_with_remainder_on_last()
    {
        // EstimateValue is the TOTAL: 100 over Jul–Sep (3 months) = 33.33 + 33.33 + 33.34, summing to 100.
        var r = New(estimateValue: 100m, type: RequisitionType.Purchase, estimateType: EstimateType.Monthly,
            start: "2026-07", end: "2026-09");
        Assert.Equal(33.33m, r.EstimatedContributionFor("2026-07"));
        Assert.Equal(33.33m, r.EstimatedContributionFor("2026-08"));
        Assert.Equal(33.34m, r.EstimatedContributionFor("2026-09"));
        var window = new[] { "2026-07", "2026-08", "2026-09" };
        Assert.Equal(100m, window.Sum(r.EstimatedContributionFor));
    }

    [Fact]
    public void Recurring_contribution_is_the_monthly_value_on_every_competency_even_when_closed()
    {
        // R4: para Conta Fixa/Recorrência, EstimateValue é o valor MENSAL — repete em cada competência da janela
        // e NÃO é rateado, mesmo com a janela encerrada (EndCompetency setado). Rateio só em Monthly não-recorrente.
        var r = New(type: RequisitionType.FixedExpense, estimateValue: 500m, estimateType: EstimateType.Monthly,
            start: "2026-07", end: "2026-09");
        Assert.Equal(500m, r.EstimatedContributionFor("2026-07"));
        Assert.Equal(500m, r.EstimatedContributionFor("2026-08"));
        Assert.Equal(500m, r.EstimatedContributionFor("2026-09"));
        Assert.Equal(0m, r.EstimatedContributionFor("2026-10"));
    }

    // ---- Total cost ------------------------------------------------------------------------------

    [Fact]
    public void TotalCost_without_installments_is_estimate() =>
        Assert.Equal(100m, New(estimateValue: 100m).TotalCost());

    [Fact]
    public void TotalCost_is_the_estimate_value_the_total() =>
        // EstimateValue is the TOTAL now — the installment count DIVIDES it, never multiplies it.
        Assert.Equal(400m, New(estimateValue: 400m, installments: 4).TotalCost());

    [Fact]
    public void TotalCost_is_the_estimate_value_regardless_of_single_installment() =>
        Assert.Equal(100m, New(estimateValue: 100m, installments: 1).TotalCost());

    // ---- Helpers ---------------------------------------------------------------------------------

    /// <summary>Submit a Draft — always moves to Triage (AEL-841).</summary>
    private static void Submit(Requisition r) => r.Submit(Actor, At);

    /// <summary>Submits the financial complement with a single authored installment for the given total —
    /// the total becomes the requisition's value (the "programado").</summary>
    private static void SubmitFC(Requisition r, decimal total) =>
        r.SubmitFinancialComplement(Actor, At, Due, [(Due, total, PaymentMethod.Pix, null, null)]);

    /// <summary>Front-loads a single-installment financial plan for the given total (Onda 2) — no transition.</summary>
    private static void SetPlan(Requisition r, decimal total, DateOnly? issue = null) =>
        r.SetFinancialPlan(issue ?? Due, [(Due, total, PaymentMethod.Pix, null, null)]);

    // ---- Project link (AEL-792) ------------------------------------------------------------------

    [Fact]
    public void New_requisition_has_no_project() => Assert.Null(New().ProjectId);

    [Fact]
    public void LinkToProject_sets_the_project_id()
    {
        var r = New();
        var project = Guid.NewGuid();

        r.LinkToProject(project);

        Assert.Equal(project, r.ProjectId);
    }

    [Fact]
    public void UnlinkProject_clears_the_link()
    {
        var r = New();
        r.LinkToProject(Guid.NewGuid());

        r.UnlinkProject();

        Assert.Null(r.ProjectId);
    }

    [Fact]
    public void LinkToProject_with_empty_id_throws() =>
        Assert.Throws<ArgumentException>(() => New().LinkToProject(Guid.Empty));

    [Fact]
    public void LinkToProject_is_blocked_once_the_requisition_left_draft()
    {
        var r = Drive(RequisitionStatus.Approval);

        var ex = Assert.Throws<RequisitionException>(() => r.LinkToProject(Guid.NewGuid()));
        Assert.Equal(RequisitionErrors.NotEditable.Code, ex.Error.Code);
    }

    // ---- Vendor + first due date (AEL-793) -------------------------------------------------------

    [Fact]
    public void Constructor_rejects_empty_vendor() =>
        Assert.Throws<ArgumentException>(() => New(vendorId: Guid.Empty));

    [Fact]
    public void Constructor_sets_vendor_and_first_due_date()
    {
        var vendor = Guid.NewGuid();
        var r = New(vendorId: vendor, firstDue: new DateOnly(2026, 9, 1));
        Assert.Equal(vendor, r.VendorId);
        Assert.Equal(new DateOnly(2026, 9, 1), r.FirstDueDate);
    }

    [Fact]
    public void Update_rejects_empty_vendor()
    {
        var r = New();
        Assert.Throws<ArgumentException>(() =>
            r.Update(RequisitionType.Purchase, "x", 1m, EstimateType.Full, "2026-07", Guid.Empty, new DateOnly(2026, 7, 10), null, null, null, null));
    }

    // ---- Finance payment feedback / realizado (AEL-793) ------------------------------------------

    [Fact]
    public void New_requisition_has_zero_realizado() => Assert.Equal(0m, New().RealizedValue);

    [Fact]
    public void RegisterPayment_accumulates_realizado_without_marking_paid_when_partial()
    {
        var r = Drive(RequisitionStatus.InPayment, estimateValue: 1000m);
        r.RegisterPayment(Guid.NewGuid(), Guid.NewGuid(), 400m, "2026-07", At);
        Assert.Equal(400m, r.RealizedValue);
        Assert.Equal(RequisitionStatus.InPayment, r.Status);
    }

    [Fact]
    public void RegisterPayment_is_idempotent_on_payment_id()
    {
        var r = Drive(RequisitionStatus.InPayment, estimateValue: 1000m);
        var paymentId = Guid.NewGuid();
        r.RegisterPayment(paymentId, Guid.NewGuid(), 400m, "2026-07", At);
        r.RegisterPayment(paymentId, Guid.NewGuid(), 400m, "2026-07", At); // redelivery — must not double-count
        Assert.Equal(400m, r.RealizedValue);
        Assert.Single(r.Payments);
    }

    [Fact]
    public void RegisterPayment_marks_paid_when_fully_settled()
    {
        var r = Drive(RequisitionStatus.InPayment, estimateValue: 1000m); // TotalCost == 1000
        r.RegisterPayment(Guid.NewGuid(), Guid.NewGuid(), 1000m, "2026-07", At);
        Assert.Equal(RequisitionStatus.Paid, r.Status);
    }

    [Fact]
    public void RegisterPayment_marks_paid_across_multiple_installments()
    {
        var r = Drive(RequisitionStatus.InPayment, estimateValue: 1000m);
        r.RegisterPayment(Guid.NewGuid(), Guid.NewGuid(), 600m, "2026-07", At);
        Assert.Equal(RequisitionStatus.InPayment, r.Status);
        r.RegisterPayment(Guid.NewGuid(), Guid.NewGuid(), 400m, "2026-08", At);
        Assert.Equal(RequisitionStatus.Paid, r.Status);
    }

    [Fact]
    public void RegisterPayment_never_settles_an_open_recurring_requisition()
    {
        // Conta Fixa/Recorrência aberta: paga o mês inteiro mas permanece InPayment aguardando a próxima
        // fatura — nunca finaliza por pagamento (espelha firebase.ts:171 do legado).
        var r = New(type: RequisitionType.FixedExpense, estimateValue: 1000m, estimateType: EstimateType.Monthly, start: "2026-07");
        Submit(r);
        r.ApproveTriage(Actor, At);
        SubmitFC(r, 1000m);
        r.Approve(Actor, At);
        Assert.Equal(RequisitionStatus.InPayment, r.Status);

        r.RegisterPayment(Guid.NewGuid(), Guid.NewGuid(), 1000m, "2026-07", At);
        Assert.Equal(RequisitionStatus.InPayment, r.Status); // não finaliza
        Assert.Equal(1000m, r.RealizedValue);
    }

    [Fact]
    public void ReversePayment_reopens_a_paid_requisition()
    {
        var r = Drive(RequisitionStatus.InPayment, estimateValue: 1000m);
        var paymentId = Guid.NewGuid();
        r.RegisterPayment(paymentId, Guid.NewGuid(), 1000m, "2026-07", At);
        Assert.Equal(RequisitionStatus.Paid, r.Status);

        r.ReversePayment(paymentId, At);
        Assert.Equal(0m, r.RealizedValue);
        Assert.Equal(RequisitionStatus.InPayment, r.Status);
    }

    [Fact]
    public void ReversePayment_is_idempotent_and_ignores_unknown()
    {
        var r = Drive(RequisitionStatus.InPayment, estimateValue: 1000m);
        var paymentId = Guid.NewGuid();
        r.RegisterPayment(paymentId, Guid.NewGuid(), 500m, "2026-07", At);
        r.ReversePayment(paymentId, At);
        r.ReversePayment(paymentId, At);      // already reversed — no-op
        r.ReversePayment(Guid.NewGuid(), At); // unknown — no-op
        Assert.Equal(0m, r.RealizedValue);
    }

    [Fact]
    public void RealizedContributionFor_buckets_by_competency()
    {
        var r = Drive(RequisitionStatus.InPayment, estimateValue: 1000m);
        r.RegisterPayment(Guid.NewGuid(), Guid.NewGuid(), 300m, "2026-07", At);
        r.RegisterPayment(Guid.NewGuid(), Guid.NewGuid(), 200m, "2026-08", At);
        Assert.Equal(300m, r.RealizedContributionFor("2026-07"));
        Assert.Equal(200m, r.RealizedContributionFor("2026-08"));
        Assert.Equal(0m, r.RealizedContributionFor("2026-09"));
    }

    [Fact]
    public void Reversed_payment_excluded_from_realized_contribution()
    {
        var r = Drive(RequisitionStatus.InPayment, estimateValue: 1000m);
        var p = Guid.NewGuid();
        r.RegisterPayment(p, Guid.NewGuid(), 300m, "2026-07", At);
        r.ReversePayment(p, At);
        Assert.Equal(0m, r.RealizedContributionFor("2026-07"));
    }

    // ---- Payment schedule for the generated Payable (AEL-793) ------------------------------------

    [Fact]
    public void BuildPaymentSchedule_single_installment_when_no_count()
    {
        var r = New(estimateValue: 900m, firstDue: new DateOnly(2026, 7, 10));
        var row = Assert.Single(r.BuildPaymentSchedule());
        Assert.Equal(1, row.Sequence);
        Assert.Equal(new DateOnly(2026, 7, 10), row.DueDate);
        Assert.Equal(900m, row.Amount);
    }

    [Fact]
    public void BuildPaymentSchedule_monthly_steps_by_one_month()
    {
        // EstimateValue is the TOTAL (900) split into 3 monthly installments of 300.
        var r = New(estimateValue: 900m, frequency: PaymentFrequency.Monthly, installments: 3,
            end: "2026-09", firstDue: new DateOnly(2026, 7, 15));
        var plan = r.BuildPaymentSchedule();
        Assert.Equal(3, plan.Count);
        Assert.Equal(new DateOnly(2026, 7, 15), plan[0].DueDate);
        Assert.Equal(new DateOnly(2026, 8, 15), plan[1].DueDate);
        Assert.Equal(new DateOnly(2026, 9, 15), plan[2].DueDate);
        Assert.Equal(900m, plan.Sum(p => p.Amount)); // sum == total (EstimateValue)
        Assert.All(plan, p => Assert.Equal(300m, p.Amount));
    }

    [Fact]
    public void BuildPaymentSchedule_puts_the_rounding_residual_on_the_last_installment()
    {
        // 100 total in 3 installments = 33.33 + 33.33 + 33.34, summing to EXACTLY 100 (no lost cents).
        var r = New(estimateValue: 100m, frequency: PaymentFrequency.Monthly, installments: 3,
            end: "2026-09", firstDue: new DateOnly(2026, 7, 10));
        var plan = r.BuildPaymentSchedule();
        Assert.Equal(new[] { 33.33m, 33.33m, 33.34m }, plan.Select(p => p.Amount));
        Assert.Equal(100m, plan.Sum(p => p.Amount));
    }

    [Fact]
    public void BuildPaymentSchedule_biweekly_steps_by_fifteen_days()
    {
        // EstimateValue is the TOTAL (100) split into 4 installments of 25; dates step every 15 days.
        var r = New(estimateValue: 100m, frequency: PaymentFrequency.Biweekly, installments: 4,
            end: "2026-08", firstDue: new DateOnly(2026, 7, 1));
        var plan = r.BuildPaymentSchedule();
        Assert.Equal(4, plan.Count);
        Assert.Equal(new DateOnly(2026, 7, 1), plan[0].DueDate);
        Assert.Equal(new DateOnly(2026, 7, 16), plan[1].DueDate);
        Assert.Equal(new DateOnly(2026, 7, 31), plan[2].DueDate);
        Assert.Equal(new DateOnly(2026, 8, 15), plan[3].DueDate);
        Assert.Equal(100m, plan.Sum(p => p.Amount)); // sum == total (EstimateValue)
    }

    [Fact]
    public void BuildPaymentSchedule_open_ended_recurrence_is_single_first_cycle()
    {
        // Recorrente é sempre Mensal sem nº de parcelas (RecurringPlanInvalid) — o plano materializa só o
        // primeiro ciclo; os meses seguintes são lançados como faturas (AEL-877).
        var r = New(type: RequisitionType.FixedExpense, estimateValue: 250m, estimateType: EstimateType.Monthly,
            frequency: PaymentFrequency.Monthly, end: null, firstDue: new DateOnly(2026, 7, 5));
        var row = Assert.Single(r.BuildPaymentSchedule());
        Assert.Equal(250m, row.Amount);
        Assert.Equal(new DateOnly(2026, 7, 5), row.DueDate);
    }

    // ---- Recurrence cycles (Conta Fixa / Recorrência), AEL-877 -----------------------------------

    /// <summary>Drives a fresh OPEN recurrence to InPayment (monthly estimate = the per-month value).</summary>
    private static Requisition DriveRecurring(decimal monthly = 1000m, RequisitionType type = RequisitionType.FixedExpense)
    {
        var r = New(type: type, estimateValue: monthly, estimateType: EstimateType.Monthly, start: "2026-07");
        Submit(r);
        r.ApproveTriage(Actor, At);
        SubmitFC(r, monthly);
        r.Approve(Actor, At);
        return r; // InPayment, open-ended
    }

    [Fact]
    public void Recurring_requires_monthly_estimate_without_installments_count()
    {
        // P1 (AEL-877): recorrente ⇒ estimativa Mensal (valor POR MÊS) e sem nº de parcelas — um count faria o
        // valor digitado virar TOTAL e inflaria a previsão mensal (o legado coage estes campos da mesma forma).
        var full = Assert.Throws<RequisitionException>(() =>
            New(type: RequisitionType.FixedExpense, estimateType: EstimateType.Full));
        Assert.Equal(RequisitionErrors.RecurringPlanInvalid.Code, full.Error.Code);

        var withCount = Assert.Throws<RequisitionException>(() =>
            New(type: RequisitionType.FixedExpense, estimateType: EstimateType.Monthly, installments: 12));
        Assert.Equal(RequisitionErrors.RecurringPlanInvalid.Code, withCount.Error.Code);

        // Mensal sem parcelas (janela aberta ou fechada) é o plano válido.
        _ = New(type: RequisitionType.FixedExpense, estimateType: EstimateType.Monthly);
        _ = New(type: RequisitionType.FixedExpense, estimateType: EstimateType.Monthly, end: "2026-12");
    }

    [Fact]
    public void SubmitFinancialComplement_recurring_authors_exactly_one_installment()
    {
        // P3 (AEL-877): o complemento de um recorrente autora só a PRIMEIRA fatura — várias parcelas fariam a
        // soma virar "total" e a Payable base cobrir meses que depois colidiriam com os ciclos.
        var r = New(type: RequisitionType.FixedExpense, estimateValue: 1000m, estimateType: EstimateType.Monthly, start: "2026-07");
        Submit(r);
        r.ApproveTriage(Actor, At);
        var ex = Assert.Throws<RequisitionException>(() =>
            r.SubmitFinancialComplement(Actor, At, new DateOnly(2026, 7, 1),
            [
                (new DateOnly(2026, 7, 10), 500m, PaymentMethod.Pix, null, null),
                (new DateOnly(2026, 8, 10), 500m, PaymentMethod.Pix, null, null),
            ]));
        Assert.Equal(RequisitionErrors.RecurringSingleInstallment.Code, ex.Error.Code);
        Assert.Equal(RequisitionStatus.FinancialComplement, r.Status); // unchanged
    }

    [Fact]
    public void RemoveCycleForCancelledPayable_frees_the_cycle_and_settles_a_terminated_recurrence()
    {
        // P2 (AEL-877): a fatura cujo título foi cancelado no Financeiro nunca será paga — sai da conta do
        // encerramento e libera a competência para relançamento.
        var r = DriveRecurring(monthly: 1000m);
        var cycle = r.AddCycle("2026-08", 500m, new DateOnly(2026, 8, 10), PaymentMethod.Pix, null, Actor, At);
        var cyclePayable = Guid.NewGuid();
        cycle.LinkPayable(cyclePayable);

        r.RegisterPayment(Guid.NewGuid(), Guid.NewGuid(), 1000m, "2026-07", At); // só o base pago
        r.TerminateRecurrence("2026-08", Actor, At);
        Assert.Equal(RequisitionStatus.InPayment, r.Status); // ciclo de agosto em aberto

        Assert.False(r.RemoveCycleForCancelledPayable("2026-09", cyclePayable, At)); // competência sem ciclo → no-op
        Assert.False(r.RemoveCycleForCancelledPayable("2026-08", Guid.NewGuid(), At)); // cancel velho ≠ payable linkado → não remove
        Assert.True(r.RemoveCycleForCancelledPayable("2026-08", cyclePayable, At));
        Assert.Empty(r.Cycles); // competência liberada
        Assert.Equal(RequisitionStatus.Paid, r.Status); // encerrada + tudo que restou coberto → quita
        Assert.Contains(r.Transitions, t => t.Action == RequisitionAction.RemoveRecurrenceCycle);
    }

    [Fact]
    public void AddCycle_launches_a_monthly_invoice_without_changing_status()
    {
        var r = DriveRecurring();
        var c = r.AddCycle("2026-08", 850m, new DateOnly(2026, 8, 10), PaymentMethod.Boleto, "846700000017599200241114202410000000000000000000", Actor, At);
        Assert.Equal(RequisitionStatus.InPayment, r.Status);
        Assert.Equal("2026-08", Assert.Single(r.Cycles).Competency);
        Assert.Equal(850m, c.Amount);
        Assert.Contains(r.Transitions, t =>
            t.Action == RequisitionAction.AddRecurrenceCycle && t.FromStatus == RequisitionStatus.InPayment && t.ToStatus == RequisitionStatus.InPayment);
    }

    [Fact]
    public void AddCycle_rejects_competency_at_or_before_start()
    {
        var r = DriveRecurring();
        var ex = Assert.Throws<RequisitionException>(() =>
            r.AddCycle("2026-07", 100m, new DateOnly(2026, 7, 10), PaymentMethod.Pix, null, Actor, At));
        Assert.Equal(RequisitionErrors.CycleBeforeStart.Code, ex.Error.Code);
    }

    [Fact]
    public void AddCycle_rejects_duplicate_competency()
    {
        var r = DriveRecurring();
        r.AddCycle("2026-08", 100m, new DateOnly(2026, 8, 10), PaymentMethod.Pix, null, Actor, At);
        var ex = Assert.Throws<RequisitionException>(() =>
            r.AddCycle("2026-08", 200m, new DateOnly(2026, 8, 15), PaymentMethod.Pix, null, Actor, At));
        Assert.Equal(RequisitionErrors.CycleCompetencyTaken.Code, ex.Error.Code);
    }

    [Fact]
    public void AddCycle_rejected_on_a_non_recurring_requisition()
    {
        var r = Drive(RequisitionStatus.InPayment, estimateValue: 1000m); // Purchase
        var ex = Assert.Throws<RequisitionException>(() =>
            r.AddCycle("2026-08", 100m, new DateOnly(2026, 8, 10), PaymentMethod.Pix, null, Actor, At));
        Assert.Equal(RequisitionErrors.NotRecurring.Code, ex.Error.Code);
    }

    [Fact]
    public void TerminateRecurrence_settles_when_base_and_cycles_are_covered()
    {
        var r = DriveRecurring(monthly: 1000m);
        r.AddCycle("2026-08", 500m, new DateOnly(2026, 8, 10), PaymentMethod.Pix, null, Actor, At);
        r.RegisterPayment(Guid.NewGuid(), Guid.NewGuid(), 1000m, "2026-07", At); // base
        r.RegisterPayment(Guid.NewGuid(), Guid.NewGuid(), 500m, "2026-08", At); // ciclo
        Assert.Equal(RequisitionStatus.InPayment, r.Status); // aberta → não quita por pagamento

        r.TerminateRecurrence("2026-08", Actor, At);
        Assert.Equal("2026-08", r.EndCompetency);
        Assert.Equal(RequisitionStatus.Paid, r.Status); // encerrada + tudo coberto → quita
    }

    [Fact]
    public void TerminateRecurrence_stays_in_payment_until_the_last_cycle_is_paid()
    {
        var r = DriveRecurring(monthly: 1000m);
        r.AddCycle("2026-08", 500m, new DateOnly(2026, 8, 10), PaymentMethod.Pix, null, Actor, At);
        r.RegisterPayment(Guid.NewGuid(), Guid.NewGuid(), 1000m, "2026-07", At); // só o base
        r.TerminateRecurrence("2026-08", Actor, At);
        Assert.Equal(RequisitionStatus.InPayment, r.Status); // ciclo de agosto ainda não pago

        r.RegisterPayment(Guid.NewGuid(), Guid.NewGuid(), 500m, "2026-08", At);
        Assert.Equal(RequisitionStatus.Paid, r.Status); // agora coberto
    }

    [Fact]
    public void Terminated_recurrence_with_partial_payments_across_cycles_settles_only_when_all_covered()
    {
        // AEL-877 (3.3): AllCyclesSettled compares the GROSS realizado against base + Σ cycles. With bounded
        // partial payments spread across the base and SEVERAL cycles, a covered month must NOT mask an uncovered
        // one — while the gross realizado is below the total due, the terminated recurrence stays InPayment; only
        // once every cycle is fully covered does it settle.
        var r = DriveRecurring(monthly: 1000m); // base due = 1000 (competency 2026-07)
        r.AddCycle("2026-08", 600m, new DateOnly(2026, 8, 10), PaymentMethod.Pix, null, Actor, At);
        r.AddCycle("2026-09", 400m, new DateOnly(2026, 9, 10), PaymentMethod.Pix, null, Actor, At);
        // total due = 1000 + 600 + 400 = 2000

        r.RegisterPayment(Guid.NewGuid(), Guid.NewGuid(), 1000m, "2026-07", At); // base 100%
        r.RegisterPayment(Guid.NewGuid(), Guid.NewGuid(), 300m, "2026-08", At); // ciclo A 50%
        // ciclo B (2026-09) em aberto → realizado 1300 < 2000
        r.TerminateRecurrence("2026-09", Actor, At);
        Assert.Equal(RequisitionStatus.InPayment, r.Status); // um mês coberto não mascara dois em aberto

        r.RegisterPayment(Guid.NewGuid(), Guid.NewGuid(), 300m, "2026-08", At); // ciclo A → 100%
        Assert.Equal(RequisitionStatus.InPayment, r.Status); // ainda falta o ciclo B (realizado 1600 < 2000)

        r.RegisterPayment(Guid.NewGuid(), Guid.NewGuid(), 400m, "2026-09", At); // ciclo B → 100%
        Assert.Equal(RequisitionStatus.Paid, r.Status); // realizado 2000 >= 2000 → quita
    }

    [Fact]
    public void TerminateRecurrence_rejects_end_before_last_cycle()
    {
        var r = DriveRecurring();
        r.AddCycle("2026-09", 100m, new DateOnly(2026, 9, 10), PaymentMethod.Pix, null, Actor, At);
        var ex = Assert.Throws<RequisitionException>(() => r.TerminateRecurrence("2026-08", Actor, At));
        Assert.Equal(RequisitionErrors.EndBeforeLastCycle.Code, ex.Error.Code);
    }

    /// <summary>Drives a fresh requisition to the requested state through valid transitions.</summary>
    private static Requisition Drive(RequisitionStatus target, decimal estimateValue = 100m)
    {
        var r = New(estimateValue: estimateValue);
        if (target == RequisitionStatus.Draft) return r;
        Submit(r);
        if (target == RequisitionStatus.Triage) return r;
        if (target == RequisitionStatus.Complement) { r.ReturnForComplement(Actor, At); return r; }
        r.ApproveTriage(Actor, At);
        if (target == RequisitionStatus.FinancialComplement) return r;
        SubmitFC(r, estimateValue);
        if (target == RequisitionStatus.Approval) return r;
        r.Approve(Actor, At);
        if (target == RequisitionStatus.InPayment) return r;
        if (target == RequisitionStatus.AwaitingFiscalDocument) { r.MarkAwaitingFiscalDocument(Actor, At); return r; }
        r.MarkPaid(Actor, At);
        if (target == RequisitionStatus.Paid) return r;
        r.MarkFinalized(Actor, At);
        if (target == RequisitionStatus.Finalized) return r;
        throw new ArgumentException($"Cannot drive to {target}.", nameof(target));
    }
}
