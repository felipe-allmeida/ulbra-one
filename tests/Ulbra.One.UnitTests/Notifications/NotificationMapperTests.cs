using Ulbra.One.Budgeting.Application.Messaging;
using Ulbra.One.Notifications.Infrastructure.Messaging;
using Xunit;

namespace Ulbra.One.UnitTests.Notifications;

/// <summary>The transition → notification mapping is a pure function, so the full matrix (audience per
/// ToStatus + the skip rules) is exercised here without a broker or a database.</summary>
public class NotificationMapperTests
{
    private static readonly Guid Company = Guid.NewGuid();
    private static readonly Guid Requisition = Guid.NewGuid();
    private static readonly Guid BudgetAdjustment = Guid.NewGuid();
    private static readonly Guid Transition = Guid.NewGuid();
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid Requester = Guid.NewGuid();
    private static readonly DateTimeOffset At = new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

    private static RequisitionTransitioned Req(string toStatus, Guid? requester = null, Guid? actor = null, string? reason = null) =>
        new(Transition, Requisition, Company, 42, "From", toStatus, "Action", actor ?? Actor, requester ?? Requester, reason, At);

    private static BudgetAdjustmentTransitioned Adq(string toStatus, string action = "Action", Guid? requester = null, Guid? actor = null) =>
        new(Transition, BudgetAdjustment, Company, 7, "From", toStatus, action, actor ?? Actor, requester ?? Requester, null, At);

    [Fact]
    public void Requisition_audit_only_transition_never_notifies()
    {
        // Recurrence actions (launch invoice / terminate, AEL-877) append InPayment→InPayment trail entries —
        // without the guard the "InPayment" arm would re-send "approved for payment" on every one of them.
        var auditOnly = new RequisitionTransitioned(
            Transition, Requisition, Company, 42, "InPayment", "InPayment", "AddRecurrenceCycle", Actor, Requester, null, At);
        Assert.Null(NotificationMapper.Map(auditOnly));
    }

    [Fact]
    public void Requisition_triage_targets_controllership_role()
    {
        var n = NotificationMapper.Map(Req("Triage"));
        Assert.NotNull(n);
        Assert.Equal("Controllership", n!.AudienceRole); // post-AEL-842: Controllership triages requisitions
        Assert.Null(n.AudienceUserId);
        Assert.Equal("RequisitionAwaitingTriage", n.Type);
        Assert.Equal(Requisition, n.ReferenceId);
        Assert.Equal(Transition, n.SourceTransitionId);
        Assert.Contains("#42", n.Title);
    }

    [Fact]
    public void Requisition_approval_targets_approver_role()
    {
        var n = NotificationMapper.Map(Req("Approval"));
        Assert.Equal("Approver", n!.AudienceRole); // AEL-883: Approver approves requisitions
        Assert.Equal("RequisitionAwaitingApproval", n.Type);
    }

    [Fact]
    public void Requisition_financial_complement_targets_requester()
    {
        var n = NotificationMapper.Map(Req("FinancialComplement"));
        Assert.Equal(Requester, n!.AudienceUserId);
        Assert.Null(n.AudienceRole);
        Assert.Equal("RequisitionAwaitingFinancialComplement", n.Type);
    }

    [Fact]
    public void Requisition_complement_targets_requester_with_reason()
    {
        var n = NotificationMapper.Map(Req("Complement", reason: "faltou o orçamento"));
        Assert.Equal(Requester, n!.AudienceUserId);
        Assert.Null(n.AudienceRole);
        Assert.Equal("RequisitionReturnedForComplement", n.Type);
        Assert.Contains("faltou o orçamento", n.Message);
    }

    [Fact]
    public void Requisition_in_payment_targets_requester()
    {
        var n = NotificationMapper.Map(Req("InPayment"));
        Assert.Equal(Requester, n!.AudienceUserId);
        Assert.Equal("RequisitionApproved", n.Type);
    }

    [Fact]
    public void Requisition_cancelled_targets_requester()
    {
        var n = NotificationMapper.Map(Req("Cancelled"));
        Assert.Equal("RequisitionCancelled", n!.Type);
        Assert.Equal(Requester, n.AudienceUserId);
    }

    [Theory]
    [InlineData("Paid")]
    [InlineData("Finalized")]
    [InlineData("AwaitingFiscalDocument")]
    public void Requisition_unmapped_status_yields_nothing(string toStatus) =>
        Assert.Null(NotificationMapper.Map(Req(toStatus)));

    [Fact]
    public void Personal_notification_skipped_when_requester_is_null() =>
        Assert.Null(NotificationMapper.Map(Req("InPayment", requester: null) with { RequesterId = null }));

    [Fact]
    public void Personal_notification_skipped_when_actor_is_the_requester()
    {
        // Anti-self rule: if the actor IS the requester, no personal notification to themselves.
        Assert.Null(NotificationMapper.Map(Req("InPayment", requester: Requester, actor: Requester)));
    }

    [Fact]
    public void Role_notification_not_skipped_even_when_actor_equals_requester()
    {
        // Triage is a role notification, so the anti-self rule doesn't apply.
        var n = NotificationMapper.Map(Req("Triage", requester: Requester, actor: Requester));
        Assert.NotNull(n);
        Assert.Equal("Controllership", n!.AudienceRole);
    }

    [Fact]
    public void Fiscal_document_attached_targets_the_requesting_accountant()
    {
        var payable = Guid.NewGuid();
        var requestedBy = Guid.NewGuid();
        var n = NotificationMapper.Map(new RequisitionFiscalDocumentAttached(Company, requestedBy, 42, Requisition, payable, At));
        Assert.NotNull(n);
        Assert.Equal(requestedBy, n!.AudienceUserId); // the Accounting user who clicked "Request fiscal document"
        Assert.Null(n.AudienceRole);
        Assert.Equal("RequisitionFiscalDocumentAttached", n.Type);
        Assert.Equal(Requisition, n.ReferenceId); // deep-links to the requisition
        Assert.Equal(payable, n.SourceTransitionId); // idempotency key = one per payable
        Assert.Contains("#42", n.Title);
    }

    [Fact]
    public void BudgetAdjustment_creation_targets_accounting_role()
    {
        var n = NotificationMapper.Map(Adq("AwaitingTriage", action: "Create"));
        Assert.Equal("Accounting", n!.AudienceRole);
        Assert.Equal("BudgetAdjustmentAwaitingTriage", n.Type);
        Assert.Equal(BudgetAdjustment, n.ReferenceId);
    }

    [Fact]
    public void BudgetAdjustment_awaiting_approval_targets_controllership()
    {
        var n = NotificationMapper.Map(Adq("AwaitingApproval"));
        Assert.Equal("Controllership", n!.AudienceRole);
        Assert.Equal("BudgetAdjustmentAwaitingApproval", n.Type);
    }

    [Theory]
    [InlineData("Approved", "BudgetAdjustmentApproved")]
    [InlineData("Cancelled", "BudgetAdjustmentCancelled")]
    [InlineData("Reversed", "BudgetAdjustmentReversed")]
    public void BudgetAdjustment_outcomes_target_requester(string toStatus, string expectedType)
    {
        var n = NotificationMapper.Map(Adq(toStatus));
        Assert.Equal(Requester, n!.AudienceUserId);
        Assert.Equal(expectedType, n.Type);
    }
}
