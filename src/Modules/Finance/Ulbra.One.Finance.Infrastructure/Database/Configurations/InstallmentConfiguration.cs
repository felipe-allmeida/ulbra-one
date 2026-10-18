using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Common.Infrastructure.Persistence;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Infrastructure.Database.Configurations;

public sealed class InstallmentConfiguration : BaseEntityConfiguration<Installment>
{
    protected override string Schema => Schemas.Finance;
    protected override string TableName => "installments";

    protected override void ConfigureEntity(EntityTypeBuilder<Installment> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.Amount).HasPrecision(18, 2);
        e.Property(x => x.NetAmount).HasPrecision(18, 2);
        e.Property(x => x.PaidAmount).HasPrecision(18, 2);
        e.Property(x => x.IsHeld).HasDefaultValue(false);
        e.Property(x => x.BoletoDigitableLine).HasMaxLength(48);
        // Looked up on every payable/requisition write to refuse a boleto already queued for payment.
        // Not unique: a cancelled title releases its line for the vendor's re-issued slip.
        e.HasIndex(x => x.BoletoDigitableLine);
        // Looked up alongside the boleto line to refuse a charge already queued for payment (AEL-963).
        // Not unique: a cancelled title releases it, and static QRs never reach the check.
        e.HasIndex(x => x.PixPayloadEmv);
        e.Property(x => x.PixKeyOverride).HasMaxLength(100);
        e.Property(x => x.PixPayloadEmv).HasMaxLength(512);
        e.HasIndex(x => x.PayableId);
        e.HasIndex(x => x.DueDate);
        e.HasIndex(x => x.Status);
        // Payments are append-only: never cascade-delete a baixa. Restrict means an installment that
        // has payments cannot be deleted (the UpdatePayable guard blocks editing a payable with payments).
        e.HasMany(x => x.Payments).WithOne().HasForeignKey(p => p.InstallmentId).OnDelete(DeleteBehavior.Restrict);
        e.Navigation(x => x.Payments).AutoInclude();
        // Optimistic concurrency via Postgres' hidden xmin system column (no DDL created).
        // Critical: two concurrent RegisterPayment calls mutate the SAME installment row;
        // the token makes the second SaveChanges fail instead of double-paying real cash.
        e.Property<uint>("xmin").IsRowVersion().HasColumnName("xmin");
    }
}
