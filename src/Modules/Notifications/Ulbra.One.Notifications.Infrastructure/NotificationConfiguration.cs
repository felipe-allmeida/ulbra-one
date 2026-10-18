using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Common.Infrastructure.Persistence;
using Ulbra.One.Notifications.Domain.Entities;

namespace Ulbra.One.Notifications.Infrastructure;

public sealed class NotificationConfiguration : BaseEntityConfiguration<Notification>
{
    protected override string Schema => Schemas.Notifications;
    protected override string TableName => "notifications";

    protected override void ConfigureEntity(EntityTypeBuilder<Notification> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.AudienceRole).HasMaxLength(32);
        builder.Property(x => x.Type).HasMaxLength(64);
        builder.Property(x => x.Title).HasMaxLength(200);
        builder.Property(x => x.Message).HasMaxLength(1000);

        // Inbox queries filter by audience (user OR role) + order/count by OccurredAt/ReadAt.
        builder.HasIndex(x => new { x.AudienceUserId, x.OccurredAt });
        builder.HasIndex(x => new { x.AudienceRole, x.OccurredAt });
        builder.HasIndex(x => x.CompanyId);

        // Idempotency guard for the consumers: one notification per (source transition, type). The unique
        // index — not the app-level existence check — is what closes the concurrent-redelivery race.
        builder.HasIndex(x => new { x.SourceTransitionId, x.Type }).IsUnique();
    }
}
