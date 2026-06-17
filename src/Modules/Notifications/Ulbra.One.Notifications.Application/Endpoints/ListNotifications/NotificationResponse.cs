using Ulbra.One.Common.Application.Ids;
using Ulbra.One.Notifications.Domain.Entities;

namespace Ulbra.One.Notifications.Application.Endpoints.ListNotifications;

public sealed record NotificationResponse(
    NotificationId Id,
    string Type,
    string Title,
    string Message,
    Guid? ReferenceId,
    DateTimeOffset OccurredAt)
{
    public static NotificationResponse From(Notification n) =>
        new(new NotificationId(n.Id), n.Type, n.Title, n.Message, n.ReferenceId, n.OccurredAt);
}
