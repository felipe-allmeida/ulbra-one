using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Common.Infrastructure.Persistence;
using Ulbra.One.Notifications.Domain.Entities;

namespace Ulbra.One.Notifications.Infrastructure;

public sealed class NotificationConfiguration : BaseEntityConfiguration<Notification>
{
    protected override string Schema => Schemas.Notifications;

    protected override void ConfigureEntity(EntityTypeBuilder<Notification> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.AudienceRole).HasMaxLength(32);
        builder.Property(x => x.Type).HasMaxLength(64);
        builder.Property(x => x.Title).HasMaxLength(200);
        builder.Property(x => x.Message).HasMaxLength(1000);
        builder.HasIndex(x => new { x.AudienceRole, x.OccurredAt });
        builder.HasIndex(x => x.CompanyId);
        builder.HasIndex(x => new { x.ReferenceId, x.Type });
    }
}
