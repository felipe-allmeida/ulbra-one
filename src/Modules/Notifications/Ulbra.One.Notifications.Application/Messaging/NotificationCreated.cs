namespace Ulbra.One.Notifications.Application.Messaging;

/// <summary>Publicado (EF bus outbox) quando uma notificação foi de fato inserida — nunca em redelivery
/// deduplicada. Existe para o realtime: carrega só o endereçamento, porque o sinal que chega ao cliente
/// é magro e ele refaz o fetch do inbox, que já autoriza (§3.3 do design). Exatamente um entre
/// <see cref="AudienceUserId"/> e <see cref="AudienceRole"/> vem preenchido, como na entidade.</summary>
public sealed record NotificationCreated(Guid NotificationId, Guid? AudienceUserId, string? AudienceRole);
