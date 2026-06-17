using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Infrastructure.Database.Configurations;

public sealed class BankAccountConfiguration : IEntityTypeConfiguration<BankAccount>
{
    public void Configure(EntityTypeBuilder<BankAccount> e)
    {
        e.Metadata.SetSchema("finance");
        e.HasKey(x => x.Id);
        e.Property(x => x.BankCode).HasMaxLength(3).IsRequired();
        e.Property(x => x.BankName).HasMaxLength(100).IsRequired();
        e.Property(x => x.Agency).HasMaxLength(10).IsRequired();
        e.Property(x => x.AccountNumber).HasMaxLength(20).IsRequired();
        e.Property(x => x.AccountDigit).HasMaxLength(2);
        e.Property(x => x.Alias).HasMaxLength(60).IsRequired();
        e.Property(x => x.OpeningBalance).HasPrecision(18, 2);
        e.Property(x => x.CreatedBy).HasMaxLength(64);
        e.Property(x => x.UpdatedBy).HasMaxLength(64);
        e.HasIndex(x => x.CompanyId);
        e.HasIndex(x => new { x.CompanyId, x.BankCode, x.Agency, x.AccountNumber, x.AccountDigit })
            .IsUnique().HasFilter("deleted_at IS NULL");
        e.HasQueryFilter(x => x.DeletedAt == null);
    }
}
