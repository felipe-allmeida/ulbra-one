using Ulbra.One.Budgeting.Application.Messaging;
using Ulbra.One.Notifications.Domain.Entities;

namespace Ulbra.One.Notifications.Infrastructure.Messaging;

/// <summary>Pure mapping from a workflow transition event to the notification it should produce (or none).
/// The target audience is decided by the transition's destination status: stages that a team acts on go to
/// that role (requisition triage → Controllership, approval → Approver; adequação triage →
/// Accounting, approval → Controllership); outcomes the requester cares about go to the requester
/// personally. Personal notifications are skipped when there is no requester or the actor IS the requester
/// (they just performed the action — e.g. the requester returning/cancelling their own requisition). Kept
/// side-effect-free so the consumers stay thin and this is unit-testable in isolation.</summary>
public static class NotificationMapper
{
    public static Notification? Map(RequisitionTransitioned e) => e.FromStatus == e.ToStatus ? null : e.ToStatus switch
    {
        // ^ Audit-only entries keep from == to (recurrence actions: launch invoice / terminate, AEL-877) and never
        // notify — the "InPayment" arm below would otherwise re-send "approved for payment" on each of them.
        "Triage" => Notification.ForRole(e.CompanyId, NotificationAudiences.RequisitionTriage,
            "RequisitionAwaitingTriage", $"Requisição #{e.Code} aguardando triagem",
            "Uma requisição foi enviada e aguarda triagem.", e.RequisitionId, e.TransitionId, e.OccurredAt),

        // Triage approved → the requester must now inform the installments + emission date (financial complement).
        "FinancialComplement" => Personal(e.RequesterId, e.ActorId, r => Notification.ForUser(e.CompanyId, r,
            "RequisitionAwaitingFinancialComplement", $"Requisição #{e.Code} aguardando complemento financeiro",
            "Sua requisição passou na triagem — informe as parcelas e a data de emissão.", e.RequisitionId, e.TransitionId, e.OccurredAt)),

        "Approval" => Notification.ForRole(e.CompanyId, NotificationAudiences.RequisitionApproval,
            "RequisitionAwaitingApproval", $"Requisição #{e.Code} aguardando aprovação",
            "Uma requisição foi triada e aguarda aprovação.", e.RequisitionId, e.TransitionId, e.OccurredAt),

        "Complement" => Personal(e.RequesterId, e.ActorId, r => Notification.ForUser(e.CompanyId, r,
            "RequisitionReturnedForComplement", $"Requisição #{e.Code} devolvida para complemento",
            WithReason("Sua requisição foi devolvida para complemento.", e.Reason), e.RequisitionId, e.TransitionId, e.OccurredAt)),

        "InPayment" => Personal(e.RequesterId, e.ActorId, r => Notification.ForUser(e.CompanyId, r,
            "RequisitionApproved", $"Requisição #{e.Code} aprovada para pagamento",
            "Sua requisição foi aprovada e seguirá para pagamento.", e.RequisitionId, e.TransitionId, e.OccurredAt)),

        "Cancelled" => Personal(e.RequesterId, e.ActorId, r => Notification.ForUser(e.CompanyId, r,
            "RequisitionCancelled", $"Requisição #{e.Code} cancelada",
            WithReason("Sua requisição foi cancelada.", e.Reason), e.RequisitionId, e.TransitionId, e.OccurredAt)),

        _ => null,
    };

    /// <summary>"Request fiscal document" (AEL-865): the Accounting asked the requester to provide a payable's fiscal document.
    /// Always personal (the requester), deep-links to the requisition via the "Requisition" type prefix.</summary>
    public static Notification? Map(RequisitionFiscalDocumentRequested e) =>
        Notification.ForUser(e.CompanyId, e.RequesterId,
            "RequisitionFiscalDocumentRequested", $"Requisição #{e.Code}: envie a Nota Fiscal",
            "O Contábil solicitou a nota fiscal deste título. Anexe a NF na requisição para dar seguimento ao pagamento.",
            e.RequisitionId, e.RequestId, e.OccurredAt);

    /// <summary>The reverse of "Request fiscal document" (AEL-865): the requester attached the fiscal document, so tell the Accounting user
    /// who asked that it arrived. Personal (that user), deep-links to the requisition via the "Requisition" prefix;
    /// PayableId is the idempotency key (one per payable).</summary>
    public static Notification? Map(RequisitionFiscalDocumentAttached e) =>
        Notification.ForUser(e.CompanyId, e.RequestedById,
            "RequisitionFiscalDocumentAttached", $"Requisição #{e.Code}: Nota Fiscal anexada",
            "O Solicitante anexou a Nota Fiscal que você pediu. O título está pronto para conferência.",
            e.RequisitionId, e.PayableId, e.OccurredAt);

    /// <summary>SLA escalation (AEL-872): a stage past its business-hour SLA → role notification (Controllership
    /// for Triage, Approver for Approval). Always role-targeted; BreachId is the idempotency key.</summary>
    public static Notification? Map(RequisitionSlaBreached e)
    {
        var isTriage = e.Stage == "Triage";
        return Notification.ForRole(
            e.CompanyId,
            isTriage ? NotificationAudiences.RequisitionTriage : NotificationAudiences.RequisitionApproval,
            isTriage ? "RequisitionTriageSlaOverdue" : "RequisitionApprovalSlaOverdue",
            isTriage ? $"SLA de triagem estourado — Requisição #{e.Code}" : $"SLA de aprovação estourado — Requisição #{e.Code}",
            isTriage
                ? $"A requisição está há mais de {e.ThresholdHours}h úteis aguardando triagem."
                : $"A requisição está há mais de {e.ThresholdHours}h úteis aguardando aprovação.",
            e.RequisitionId, e.BreachId, e.OccurredAt);
    }

    public static Notification? Map(BudgetAdjustmentTransitioned e) => e.ToStatus switch
    {
        // Only the synthetic creation event lands in AwaitingTriage (real transitions never move back to it).
        "AwaitingTriage" => Notification.ForRole(e.CompanyId, NotificationAudiences.AdjustmentTriage,
            "BudgetAdjustmentAwaitingTriage", $"Adequação #{e.Code} aguardando triagem",
            "Uma adequação orçamentária foi criada e aguarda triagem.", e.BudgetAdjustmentId, e.TransitionId, e.OccurredAt),

        "AwaitingApproval" => Notification.ForRole(e.CompanyId, NotificationAudiences.AdjustmentApproval,
            "BudgetAdjustmentAwaitingApproval", $"Adequação #{e.Code} aguardando aprovação",
            "Uma adequação orçamentária foi triada e aguarda aprovação.", e.BudgetAdjustmentId, e.TransitionId, e.OccurredAt),

        "Approved" => Personal(e.RequesterId, e.ActorId, r => Notification.ForUser(e.CompanyId, r,
            "BudgetAdjustmentApproved", $"Adequação #{e.Code} aprovada",
            "Sua adequação orçamentária foi aprovada.", e.BudgetAdjustmentId, e.TransitionId, e.OccurredAt)),

        "Cancelled" => Personal(e.RequesterId, e.ActorId, r => Notification.ForUser(e.CompanyId, r,
            "BudgetAdjustmentCancelled", $"Adequação #{e.Code} cancelada",
            WithReason("Sua adequação orçamentária foi cancelada.", e.Reason), e.BudgetAdjustmentId, e.TransitionId, e.OccurredAt)),

        "Reversed" => Personal(e.RequesterId, e.ActorId, r => Notification.ForUser(e.CompanyId, r,
            "BudgetAdjustmentReversed", $"Adequação #{e.Code} estornada",
            "Sua adequação orçamentária aprovada foi estornada.", e.BudgetAdjustmentId, e.TransitionId, e.OccurredAt)),

        _ => null,
    };

    private static Notification? Personal(Guid? requesterId, Guid actorId, Func<Guid, Notification> build) =>
        requesterId is Guid r && r != actorId ? build(r) : null;

    private static string WithReason(string message, string? reason) =>
        Truncate(string.IsNullOrWhiteSpace(reason) ? message : $"{message} Motivo: {reason}", 1000);

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
