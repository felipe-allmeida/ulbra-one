using Microsoft.EntityFrameworkCore;
using Npgsql;
using Ulbra.One.Common.Application.Persistence;
using Ulbra.One.Notifications.Domain.Entities;

namespace Ulbra.One.Notifications.Infrastructure.Messaging;

/// <summary>Persists a mapped notification idempotently. The app-level existence check covers the common
/// redelivery; the unique index on <c>(SourceTransitionId, Type)</c> closes the concurrent-redelivery race,
/// and only that specific unique violation (23505) is swallowed — any other write failure propagates so the
/// message goes to retry/DLQ rather than being silently dropped. The consumer's SaveChanges writes ONLY this
/// notification, so the narrow catch can't mask an unrelated failure.</summary>
internal static class NotificationWriter
{
    public static async Task<Notification?> PersistAsync(Notification? notification, Guid transitionId, IUnitOfWork uow, CancellationToken ct)
    {
        if (notification is null) return null; // transition maps to no notification
        if (await uow.ReadSet<Notification>().AnyAsync(n => n.SourceTransitionId == transitionId, ct)) return null;

        await uow.AddAsync(notification, ct);
        try
        {
            await uow.SaveChangesAsync(ct);
            return notification;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A concurrent redelivery inserted the same (SourceTransitionId, Type) first — the notification
            // already exists, so this delivery is a no-op.
            uow.ClearChangeTracker();
            return null;
        }
    }
}
