namespace Ulbra.One.Notifications.Infrastructure.Messaging;

/// <summary>Roles that can act on each stage. Requisition and adequação workflows have different audiences
/// (AEL-842 remap) so they stay separate. Local constants (no coupling to Identity.Domain); the notification
/// persists the name in effect at creation time.</summary>
internal static class NotificationAudiences
{
    public const string RequisitionTriage = "Controllership";
    public const string RequisitionApproval = "Approver";
    public const string AdjustmentTriage = "Accounting";
    public const string AdjustmentApproval = "Controllership";
}
