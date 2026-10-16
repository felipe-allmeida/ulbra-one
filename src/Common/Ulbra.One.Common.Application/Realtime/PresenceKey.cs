namespace Ulbra.One.Common.Application.Realtime;

public readonly record struct PresenceKey(string Scope, Guid ResourceId, Guid UserId);
