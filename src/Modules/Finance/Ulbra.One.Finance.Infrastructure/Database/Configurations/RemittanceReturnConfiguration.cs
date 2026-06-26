using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Common.Infrastructure.Persistence;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Infrastructure.Database.Configurations;

public sealed class RemittanceReturnConfiguration : BaseEntityConfiguration<RemittanceReturn>
{
    protected override string Schema => Schemas.Finance;

    protected override void ConfigureEntity(EntityTypeBuilder<RemittanceReturn> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.FileStorageKey).HasMaxLength(300).IsRequired();
        e.Property(x => x.FileName).HasMaxLength(200).IsRequired();
        e.Property(x => x.ProcessedBy).HasMaxLength(100).IsRequired();
        // Optimistic concurrency via Postgres' hidden xmin system column (no DDL created).
        e.Property<uint>("xmin").IsRowVersion().HasColumnName("xmin");
    }
}
