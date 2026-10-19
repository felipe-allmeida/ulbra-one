using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Common.Domain.Auditing;

namespace Ulbra.One.Common.Infrastructure.Persistence;

/// <summary>
/// Base EF configuration that applies cross-cutting mapping by interface:
/// table + schema (always), CreatedBy/UpdatedBy max length (IAuditable), and the
/// soft-delete query filter (ISoftDeletable, unless a config opts out).
/// The table name is declared here, per config, instead of emerging from the name of
/// the matching DbSet property on the context — so the mapping survives the DbSet going
/// away. Column names stay convention-driven (UseSnakeCaseNamingConvention) — never set
/// them here.
/// </summary>
public abstract class BaseEntityConfiguration<T> : IEntityTypeConfiguration<T> where T : class
{
    protected abstract string Schema { get; }

    /// <summary>Physical table name, in snake_case, exactly as it exists in the database.</summary>
    protected abstract string TableName { get; }

    /// <summary>Legacy entities (e.g. Transaction) override to false to keep current behavior.</summary>
    protected virtual bool ApplySoftDeleteFilter => true;

    public void Configure(EntityTypeBuilder<T> builder)
    {
        // Raw annotations rather than ToTable(name, schema): this project references EF Core
        // but not EF Core Relational, where the ToTable extension lives.
        builder.HasAnnotation("Relational:TableName", TableName);
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
