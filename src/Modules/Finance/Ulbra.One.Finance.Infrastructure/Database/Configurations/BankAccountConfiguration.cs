using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Common.Infrastructure.Persistence;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Infrastructure.Database.Configurations;

public sealed class BankAccountConfiguration : BaseEntityConfiguration<BankAccount>
{
    protected override string Schema => "finance";

    protected override void ConfigureEntity(EntityTypeBuilder<BankAccount> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.BankCode).HasMaxLength(3).IsRequired();
        e.Property(x => x.BankName).HasMaxLength(100).IsRequired();
        e.Property(x => x.Agency).HasMaxLength(10).IsRequired();
        e.Property(x => x.AccountNumber).HasMaxLength(20).IsRequired();
        e.Property(x => x.AccountDigit).HasMaxLength(2);
        e.Property(x => x.Alias).HasMaxLength(60).IsRequired();
        e.Property(x => x.OpeningBalance).HasPrecision(18, 2);
        e.HasIndex(x => x.CompanyId);
        e.HasIndex(x => new { x.CompanyId, x.BankCode, x.Agency, x.AccountNumber, x.AccountDigit })
            .IsUnique().HasFilter("deleted_at IS NULL");
    }
}
