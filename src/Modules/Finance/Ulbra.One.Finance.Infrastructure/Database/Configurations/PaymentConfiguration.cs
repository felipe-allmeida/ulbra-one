using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Common.Infrastructure.Persistence;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Infrastructure.Database.Configurations;

public sealed class PaymentConfiguration : BaseEntityConfiguration<Payment>
{
    protected override string Schema => Schemas.Finance;
    protected override string TableName => "payments";

    protected override void ConfigureEntity(EntityTypeBuilder<Payment> e)
    {
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
        // The settlement history lists payments ordered by date across all payables.
        e.HasIndex(x => x.PaymentDate);
        // CSRF retentions realized at this baixa. Cascade: a payment is never deleted (append-only), but if it
        // were, its withholding rows go with it. AutoInclude so the aggregate carries them for posting/reversal.
        e.HasMany(x => x.Withholdings).WithOne().HasForeignKey(w => w.PaymentId).OnDelete(DeleteBehavior.Cascade);
        e.Navigation(x => x.Withholdings).AutoInclude();
    }
}
