using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Common.Infrastructure.Persistence;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Infrastructure.Database.Configurations;

public sealed class BankAccountConfiguration : BaseEntityConfiguration<BankAccount>
{
    protected override string Schema => Schemas.Finance;
    protected override string TableName => "bank_accounts";

    protected override void ConfigureEntity(EntityTypeBuilder<BankAccount> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.BankCode).HasMaxLength(3).IsRequired();
        e.Property(x => x.BankName).HasMaxLength(100).IsRequired();
        e.Property(x => x.Agency).HasMaxLength(10).IsRequired();
        e.Property(x => x.AccountNumber).HasMaxLength(20).IsRequired();
        e.Property(x => x.AccountDigit).HasMaxLength(2);
        e.Property(x => x.AgencyDigit).HasMaxLength(2);
        e.Property(x => x.AddressStreet).HasMaxLength(200);
        e.Property(x => x.AddressNumber).HasMaxLength(20);
        e.Property(x => x.AddressComplement).HasMaxLength(100);
        e.Property(x => x.AddressCity).HasMaxLength(100);
        e.Property(x => x.AddressCep).HasMaxLength(8);
        e.Property(x => x.AddressState).HasMaxLength(2);
        e.Property(x => x.Alias).HasMaxLength(60).IsRequired();
        e.Property(x => x.OpeningBalance).HasPrecision(18, 2);
        e.Property(x => x.ReconciledBalance).HasPrecision(18, 2);
        e.Property(x => x.CnabAgreementCode).HasMaxLength(30);
        e.Property(x => x.OfxAccountId).HasMaxLength(40);
        e.Property(x => x.PayerCnpj).HasMaxLength(14);
        e.Property(x => x.PayerLegalName).HasMaxLength(100);
        e.HasIndex(x => new { x.CompanyId, x.BankCode, x.Agency, x.AccountNumber, x.AccountDigit })
            .IsUnique().HasFilter("deleted_at IS NULL");
        // At most one default paying account per company (live rows only).
        e.HasIndex(x => x.CompanyId).IsUnique()
            .HasFilter("is_default_for_payments AND deleted_at IS NULL")
            .HasDatabaseName("ix_bank_accounts_company_default_payer");
    }
}
