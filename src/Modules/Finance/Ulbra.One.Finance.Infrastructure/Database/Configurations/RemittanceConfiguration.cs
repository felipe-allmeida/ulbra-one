using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Common.Infrastructure.Persistence;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Infrastructure.Database.Configurations;

public sealed class RemittanceConfiguration : BaseEntityConfiguration<Remittance>
{
    protected override string Schema => Schemas.Finance;
    protected override string TableName => "remittances";

    protected override void ConfigureEntity(EntityTypeBuilder<Remittance> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.BankCode).HasMaxLength(3).IsRequired();
        e.Property(x => x.GeneratedBy).HasMaxLength(100).IsRequired();
        e.Property(x => x.FileStorageKey).HasMaxLength(300);
        e.Property(x => x.FileName).HasMaxLength(200);
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        e.Property(x => x.Category).HasConversion<string>().HasMaxLength(20);
        e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.RemittanceId).OnDelete(DeleteBehavior.Cascade);
        e.Navigation(x => x.Lines).AutoInclude();
        // Unique sequential number per paying bank account (live rows only).
        e.HasIndex(x => new { x.CompanyId, x.PayingBankAccountId, x.SequenceNumber })
            .IsUnique()
            .HasFilter("deleted_at IS NULL");
        // Optimistic concurrency via Postgres' hidden xmin system column (no DDL created).
        e.Property<uint>("xmin").IsRowVersion().HasColumnName("xmin");
    }
}
