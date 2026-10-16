namespace Ulbra.One.Notifications.Domain.Entities;

/// <summary>In-app notification row. Addressed to EITHER a role (<see cref="AudienceRole"/>, e.g. the whole
/// Accounting team) OR a specific user (<see cref="AudienceUserId"/>, e.g. the solicitante) — exactly one is
/// set, guaranteed by the <see cref="ForRole"/> / <see cref="ForUser"/> factories. Read state
/// (<see cref="ReadAt"/>) is shared: for a role notification, when anyone on the team marks it read it clears
/// for the team (it's a work-queue signal, not a per-person inbox). <see cref="SourceTransitionId"/> is the
/// esteira transition that produced it — the consumer's idempotency key. <see cref="ReferenceId"/> is a loose
/// Guid into the originating aggregate (requisition/adequação), no cross-schema FK. Deliberately NOT
/// <c>IAuditable</c> (no CreatedBy/UpdatedBy) so read-all can use a set-based ExecuteUpdate.</summary>
public sealed class Notification
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CompanyId { get; private set; }

    /// <summary>Target role name (e.g. "Accounting"), or null when the notification is user-targeted.</summary>
    public string? AudienceRole { get; private set; }

    /// <summary>Target user id, or null when the notification is role-targeted.</summary>
    public Guid? AudienceUserId { get; private set; }

    public string Type { get; private set; } = "";
    public string Title { get; private set; } = "";
    public string Message { get; private set; } = "";

    /// <summary>The originating aggregate (RequisitionId / BudgetAdjustmentId) — loose Guid for the deep link.</summary>
    public Guid? ReferenceId { get; private set; }

    /// <summary>The esteira transition that produced this notification — idempotency key (unique with Type).</summary>
    public Guid SourceTransitionId { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>When this was marked read (shared across the audience). Null = unread.</summary>
    public DateTimeOffset? ReadAt { get; private set; }

    private Notification() { }

    private Notification(Guid companyId, string? audienceRole, Guid? audienceUserId, string type,
        string title, string message, Guid? referenceId, Guid sourceTransitionId, DateTimeOffset occurredAt)
    {
        CompanyId = companyId;
        AudienceRole = audienceRole;
        AudienceUserId = audienceUserId;
        Type = type;
        Title = title;
        Message = message;
        ReferenceId = referenceId;
        SourceTransitionId = sourceTransitionId;
        OccurredAt = occurredAt;
    }

    /// <summary>Creates a role-targeted notification (the whole team with that role sees it).</summary>
    public static Notification ForRole(Guid companyId, string audienceRole, string type, string title,
        string message, Guid? referenceId, Guid sourceTransitionId, DateTimeOffset occurredAt)
    {
        if (string.IsNullOrWhiteSpace(audienceRole)) throw new ArgumentException("AudienceRole is required.", nameof(audienceRole));
        return new Notification(companyId, audienceRole, null, type, title, message, referenceId, sourceTransitionId, occurredAt);
    }

    /// <summary>Creates a user-targeted notification (a single person, e.g. the solicitante).</summary>
    public static Notification ForUser(Guid companyId, Guid audienceUserId, string type, string title,
        string message, Guid? referenceId, Guid sourceTransitionId, DateTimeOffset occurredAt)
    {
        if (audienceUserId == Guid.Empty) throw new ArgumentException("AudienceUserId is required.", nameof(audienceUserId));
        return new Notification(companyId, null, audienceUserId, type, title, message, referenceId, sourceTransitionId, occurredAt);
    }

    /// <summary>Marks the notification read. Idempotent — a no-op if already read (keeps the first timestamp).</summary>
    public void MarkRead(DateTimeOffset at) => ReadAt ??= at;
}
