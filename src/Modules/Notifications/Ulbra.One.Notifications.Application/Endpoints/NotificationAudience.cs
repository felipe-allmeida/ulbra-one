using System.Linq.Expressions;
using Ulbra.One.Notifications.Domain.Entities;

namespace Ulbra.One.Notifications.Application.Endpoints;

/// <summary>The single audience predicate shared by every inbox query — a caller sees a notification when it
/// targets them personally, targets their role, or (Admin) targets any role. Kept as one expression so the
/// list, unread-count, mark-read and read-all endpoints can't drift apart. Guards <paramref name="role"/>
/// against null so a missing role claim never turns <c>AudienceRole == role</c> into
/// <c>AudienceRole IS NULL</c> (which would match other users' personal notifications).</summary>
public static class NotificationAudience
{
    public static Expression<Func<Notification, bool>> For(Guid userId, string? role, bool isAdmin) =>
        n => n.AudienceUserId == userId
             || (role != null && n.AudienceRole == role)
             || (isAdmin && n.AudienceRole != null);
}
