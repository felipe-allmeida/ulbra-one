namespace Ulbra.One.Notifications.Domain.Entities;

/// <summary>In-app notification row. Audience-by-role; no read-state in v1.</summary>
public sealed class Notification
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid CompanyId { get; private set; }
    public string AudienceRole { get; private set; } = "";  // e.g. "Admin"
    public string Type { get; private set; } = "";          // e.g. "PayablePendingApproval"
    public string Title { get; private set; } = "";
    public string Message { get; private set; } = "";
    public Guid? ReferenceId { get; private set; }          // PayableId, loose reference
    public DateTimeOffset OccurredAt { get; private set; }

    private Notification() { }

    public Notification(Guid companyId, string audienceRole, string type, string title, string message,
        Guid? referenceId, DateTimeOffset occurredAt)
    {
        CompanyId = companyId;
        AudienceRole = audienceRole;
        Type = type;
        Title = title;
        Message = message;
        ReferenceId = referenceId;
        OccurredAt = occurredAt;
    }
}
