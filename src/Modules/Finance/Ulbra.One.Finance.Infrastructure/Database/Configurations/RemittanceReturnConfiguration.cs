using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Common.Infrastructure.Persistence;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Infrastructure.Database.Configurations;

public sealed class RemittanceReturnConfiguration : BaseEntityConfiguration<RemittanceReturn>
{
    protected override string Schema => Schemas.Finance;
    protected override string TableName => "remittance_returns";

    protected override void ConfigureEntity(EntityTypeBuilder<RemittanceReturn> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.FileStorageKey).HasMaxLength(300).IsRequired();
        e.Property(x => x.FileName).HasMaxLength(200).IsRequired();
        e.Property(x => x.ProcessedBy).HasMaxLength(100).IsRequired();
        // Pre-existing rows all came from the per-remittance import, which only records an applied baixa.
        e.Property(x => x.Applied).HasDefaultValue(true);
        // The history is always browsed by paying account, newest first.
        e.HasIndex(x => new { x.PayingBankAccountId, x.ProcessedAt });
        // Optimistic concurrency via Postgres' hidden xmin system column (no DDL created).
        e.Property<uint>("xmin").IsRowVersion().HasColumnName("xmin");
    }
}
