using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Common.Infrastructure.Persistence;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Registry.Infrastructure.Database.Configurations;

public sealed class VendorConfiguration : BaseEntityConfiguration<VendorEntity>
{
    protected override string Schema => Schemas.Registry;

    protected override void ConfigureEntity(EntityTypeBuilder<VendorEntity> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.Name).HasMaxLength(200).IsRequired();
        e.Property(x => x.TradeName).HasMaxLength(200);
        e.Property(x => x.Document).HasMaxLength(14).IsRequired();
        e.Property(x => x.Email).HasMaxLength(200);
        e.Property(x => x.Phone).HasMaxLength(20);
        e.Property(x => x.PaymentTerms).HasMaxLength(100);
        e.Property(x => x.TaxRegime).HasConversion<string>().HasMaxLength(20);
        e.OwnsOne(x => x.Address, AddressConfiguration.Configure);
        e.OwnsOne(x => x.BankInfo, BankInfoConfiguration.Configure);
        // Partial unique index: uniqueness applies only to live (non-soft-deleted) rows,
        // so the same document can be re-registered after a soft-delete.
        e.HasIndex(x => new { x.CompanyId, x.Document }).IsUnique().HasFilter("deleted_at IS NULL");
    }
}
