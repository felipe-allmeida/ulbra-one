using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Infrastructure.Persistence;

namespace Ulbra.One.Budgeting.Infrastructure.Database.Configurations;

public sealed class RequisitionPaymentConfiguration : BaseEntityConfiguration<RequisitionPayment>
{
    protected override string Schema => Schemas.Budgeting;
    protected override string TableName => "requisition_payment";

    protected override void ConfigureEntity(EntityTypeBuilder<RequisitionPayment> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.Amount).HasPrecision(18, 2);
        e.Property(x => x.Competency).HasMaxLength(7).IsRequired();
        e.HasIndex(x => x.RequisitionId);
        // PaymentId is the Finance baixa id and the integration's dedup key — unique so a duplicate
        // delivery can never double-count the "realizado".
        e.HasIndex(x => x.PaymentId).IsUnique();
    }
}
