using Microsoft.EntityFrameworkCore;
using Ulbra.One.Audit.Domain.Entities;

namespace Ulbra.One.Audit.Infrastructure;

public static class AuditLogModelConfig
{
    public static void MapAuditLog(this ModelBuilder modelBuilder, bool ownsTable)
    {
        modelBuilder.Entity<AuditLog>(e =>
        {
            e.ToTable("audit_log", "audit", t => { if (!ownsTable) t.ExcludeFromMigrations(); });
            e.HasKey(x => x.Id);
            e.Property(x => x.EntityType).HasMaxLength(200);
            e.Property(x => x.Schema).HasMaxLength(100);
            e.Property(x => x.TableName).HasMaxLength(200);
            e.Property(x => x.PrimaryKey).HasMaxLength(400);
            e.Property(x => x.ActorUserId).HasMaxLength(200);
            e.Property(x => x.TraceId).HasMaxLength(100);
            e.Property(x => x.OldValues).HasColumnType("jsonb");
            e.Property(x => x.NewValues).HasColumnType("jsonb");
            e.Property(x => x.AffectedColumns).HasColumnType("jsonb");
            e.HasIndex(x => x.OccurredAt);
            e.HasIndex(x => new { x.EntityType, x.PrimaryKey });
        });
    }
}
