using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Common.Domain.Auditing;

namespace Ulbra.One.Common.Infrastructure.Persistence;

/// <summary>
/// Base EF configuration that applies cross-cutting mapping by interface:
/// schema (always), CreatedBy/UpdatedBy max length (IAuditable), and the
/// soft-delete query filter (ISoftDeletable, unless a config opts out).
/// Table/column names stay convention-driven (UseSnakeCaseNamingConvention) —
/// never set them here.
/// </summary>
public abstract class BaseEntityConfiguration<T> : IEntityTypeConfiguration<T> where T : class
{
    protected abstract string Schema { get; }

    /// <summary>Legacy entities (e.g. Transaction) override to false to keep current behavior.</summary>
    protected virtual bool ApplySoftDeleteFilter => true;

    public void Configure(EntityTypeBuilder<T> builder)
    {
        builder.HasAnnotation("Relational:Schema", Schema);

        if (typeof(IAuditable).IsAssignableFrom(typeof(T)))
        {
            builder.Property("CreatedBy").HasMaxLength(64);
            builder.Property("UpdatedBy").HasMaxLength(64);
        }

        if (ApplySoftDeleteFilter && typeof(ISoftDeletable).IsAssignableFrom(typeof(T)))
        {
            builder.HasQueryFilter(e => EF.Property<DateTimeOffset?>(e, "DeletedAt") == null);
        }

        ConfigureEntity(builder);
    }

    protected abstract void ConfigureEntity(EntityTypeBuilder<T> builder);
}
