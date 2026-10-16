namespace Ulbra.One.Common.Application.Ids;

/// <summary>Strongly-typed, prefixed id for Notification entities (wire form <c>notification_{guid:N}</c>).</summary>
[PrefixedId("notification_")]
public readonly partial record struct NotificationId(Guid Value);
