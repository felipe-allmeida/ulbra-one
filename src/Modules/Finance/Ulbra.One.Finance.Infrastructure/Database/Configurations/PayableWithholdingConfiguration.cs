using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Common.Infrastructure.Persistence;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Infrastructure.Database.Configurations;

public sealed class PayableWithholdingConfiguration : BaseEntityConfiguration<PayableWithholding>
{
    protected override string Schema => Schemas.Finance;
    protected override string TableName => "payable_withholdings";

    protected override void ConfigureEntity(EntityTypeBuilder<PayableWithholding> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.TaxType).HasConversion<string>().HasMaxLength(20);
        e.Property(x => x.BaseAmount).HasPrecision(18, 2);
        e.Property(x => x.Rate).HasPrecision(10, 6);
        e.Property(x => x.Amount).HasPrecision(18, 2);
        e.HasIndex(x => x.PayableId);
    }
}
