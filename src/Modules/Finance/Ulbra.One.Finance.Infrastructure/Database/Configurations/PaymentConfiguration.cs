using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Infrastructure.Database.Configurations;

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> e)
    {
        e.Metadata.SetSchema("finance");
        e.HasKey(x => x.Id);
        e.Property(x => x.Amount).HasPrecision(18, 2);
        e.Property(x => x.Interest).HasPrecision(18, 2);
        e.Property(x => x.Fine).HasPrecision(18, 2);
        e.Property(x => x.Discount).HasPrecision(18, 2);
        e.Property(x => x.Notes).HasMaxLength(300);
        e.Property(x => x.ReversalReason).HasMaxLength(300);
        e.Property(x => x.ProofFileName).HasMaxLength(260);
        e.Property(x => x.ProofContentType).HasMaxLength(120);
        e.Property(x => x.ProofStoragePath).HasMaxLength(300);
        e.HasIndex(x => x.InstallmentId);
        e.HasIndex(x => x.PayableId);
        e.HasIndex(x => x.BankAccountId);
    }
}
