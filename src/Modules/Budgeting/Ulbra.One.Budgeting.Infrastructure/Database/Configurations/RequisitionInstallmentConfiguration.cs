using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Infrastructure.Persistence;

namespace Ulbra.One.Budgeting.Infrastructure.Database.Configurations;

public sealed class RequisitionInstallmentConfiguration : BaseEntityConfiguration<RequisitionInstallment>
{
    protected override string Schema => Schemas.Budgeting;
    protected override string TableName => "requisition_installment";

    protected override void ConfigureEntity(EntityTypeBuilder<RequisitionInstallment> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.Amount).HasPrecision(18, 2);
        // Payment method persisted as string (readability, matches the module's other enums). The unique
        // sequence-per-requisition is guaranteed by the aggregate, so only a lookup index here — a unique
        // (RequisitionId, Sequence) index would break the delete-then-insert re-submit within one SaveChanges.
        e.Property(x => x.PaymentMethod).HasConversion<string>().HasMaxLength(16).IsRequired();
        e.Property(x => x.BoletoDigitableLine).HasMaxLength(48);
        // Looked up on every payable/requisition write to refuse a boleto already queued for payment.
        // Not unique: a cancelled title releases its line for the vendor's re-issued slip.
        e.HasIndex(x => x.BoletoDigitableLine);
        // Looked up alongside the boleto line to refuse a charge already queued for payment (AEL-963).
        // Not unique: a cancelled title releases it.
        e.HasIndex(x => x.PixPayloadEmv);
        e.Property(x => x.PixKeyOverride).HasMaxLength(100);
        e.Property(x => x.PixPayloadEmv).HasMaxLength(512);
        e.HasIndex(x => x.RequisitionId);
    }
}
