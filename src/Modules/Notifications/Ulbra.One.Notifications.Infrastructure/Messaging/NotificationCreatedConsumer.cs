using MassTransit;
using Ulbra.One.Common.Application.Realtime;
using Ulbra.One.Notifications.Application.Messaging;

namespace Ulbra.One.Notifications.Infrastructure.Messaging;

/// <summary>Fila compartilhada (uma réplica só processa) → PUBLISH no backplane, que espalha para todas.
/// Não escreve no banco: quem grava é o consumer da transição. O core é <see cref="ProcessAsync"/> para
/// que o teste possa dirigi-lo sem broker, como nos demais consumers do módulo.</summary>
public sealed class NotificationCreatedConsumer(IRealtimeBackplane backplane) : IConsumer<NotificationCreated>
{
    public Task Consume(ConsumeContext<NotificationCreated> context) =>
        ProcessAsync(context.Message, context.CancellationToken);

    public Task ProcessAsync(NotificationCreated message, CancellationToken ct)
    {
        var target = message.AudienceUserId is { } userId
            ? RealtimeTarget.ForUser(userId)
            : RealtimeTarget.ForRole(message.AudienceRole!);

        return backplane.PublishAsync(
            new RealtimeEnvelope(target, new RealtimeSignal("notification", message.NotificationId.ToString())), ct);
    }
}
