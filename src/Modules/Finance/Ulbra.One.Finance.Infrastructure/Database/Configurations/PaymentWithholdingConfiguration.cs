using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Common.Infrastructure.Persistence;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Infrastructure.Database.Configurations;

public sealed class PaymentWithholdingConfiguration : BaseEntityConfiguration<PaymentWithholding>
{
    protected override string Schema => Schemas.Finance;
    protected override string TableName => "payment_withholding";

    protected override void ConfigureEntity(EntityTypeBuilder<PaymentWithholding> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.TaxType).HasConversion<string>().HasMaxLength(20);
        e.Property(x => x.BaseAmount).HasPrecision(18, 2);
        e.Property(x => x.Amount).HasPrecision(18, 2);
        e.HasIndex(x => x.PaymentId);
    }
}
