using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Infrastructure.Persistence;

namespace Ulbra.One.Budgeting.Infrastructure.Database.Configurations;

public sealed class RequisitionCycleConfiguration : BaseEntityConfiguration<RequisitionCycle>
{
    protected override string Schema => Schemas.Budgeting;
    protected override string TableName => "requisition_cycle";

    protected override void ConfigureEntity(EntityTypeBuilder<RequisitionCycle> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.Competency).HasMaxLength(7).IsRequired();
        e.Property(x => x.Amount).HasPrecision(18, 2);
        e.Property(x => x.PaymentMethod).HasConversion<string>().HasMaxLength(16).IsRequired();
        e.Property(x => x.BoletoDigitableLine).HasMaxLength(48);
        e.Property(x => x.PixKeyOverride).HasMaxLength(100);
        e.Property(x => x.CreatedBy).HasMaxLength(64);
        // One cycle per competency per requisition — the domain idempotency key (also blocks colliding
        // launches for the same month).
        e.HasIndex(x => new { x.RequisitionId, x.Competency }).IsUnique();
    }
}
