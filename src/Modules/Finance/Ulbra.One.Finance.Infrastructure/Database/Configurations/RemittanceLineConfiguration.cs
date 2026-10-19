using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Common.Infrastructure.Persistence;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Infrastructure.Database.Configurations;

public sealed class RemittanceLineConfiguration : BaseEntityConfiguration<RemittanceLine>
{
    protected override string Schema => Schemas.Finance;
    protected override string TableName => "remittance_lines";

    protected override void ConfigureEntity(EntityTypeBuilder<RemittanceLine> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.FavoredName).HasMaxLength(200).IsRequired();
        e.Property(x => x.FavoredDocument).HasMaxLength(14).IsRequired();
        e.Property(x => x.FavoredBankCode).HasMaxLength(3).IsRequired();
        e.Property(x => x.FavoredAgency).HasMaxLength(10).IsRequired();
        e.Property(x => x.FavoredAccount).HasMaxLength(20).IsRequired();
        e.Property(x => x.FavoredAccountDigit).HasMaxLength(2);
        e.Property(x => x.FavoredPixKey).HasMaxLength(140);
        e.Property(x => x.BoletoDigitableLine).HasMaxLength(48);
        e.Property(x => x.FavoredAddressStreet).HasMaxLength(200);
        e.Property(x => x.FavoredAddressNumber).HasMaxLength(20);
        e.Property(x => x.FavoredAddressComplement).HasMaxLength(100);
        e.Property(x => x.FavoredAddressNeighborhood).HasMaxLength(100);
        e.Property(x => x.FavoredAddressCity).HasMaxLength(100);
        e.Property(x => x.FavoredAddressCep).HasMaxLength(8);
        e.Property(x => x.FavoredAddressState).HasMaxLength(2);
        e.Property(x => x.Reference).HasMaxLength(20).IsRequired();
        e.Property(x => x.Amount).HasPrecision(18, 2);
        e.Property(x => x.Method).HasConversion<string>().HasMaxLength(20);
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        e.Property(x => x.BankReference).HasMaxLength(20);
        e.Property(x => x.AuthCode).HasMaxLength(64);
        e.Property(x => x.ReturnReason).HasMaxLength(200);
        e.HasIndex(x => x.RemittanceId);
    }
}
