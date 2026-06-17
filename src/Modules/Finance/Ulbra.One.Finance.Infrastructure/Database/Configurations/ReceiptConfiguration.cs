using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Common.Infrastructure.Persistence;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Infrastructure.Database.Configurations;

public sealed class ReceiptConfiguration : BaseEntityConfiguration<Receipt>
{
    protected override string Schema => Schemas.Finance;

    protected override void ConfigureEntity(EntityTypeBuilder<Receipt> e)
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
        e.HasIndex(x => x.ReceivableId);
        e.HasIndex(x => x.BankAccountId);
        // FK on the denormalized receivable_id shortcut (the aggregate root), in addition to the
        // installment_id FK — prevents orphan receipts if a receivable is ever hard-deleted.
        e.HasOne<Receivable>().WithMany().HasForeignKey(x => x.ReceivableId).OnDelete(DeleteBehavior.Cascade);
    }
}
