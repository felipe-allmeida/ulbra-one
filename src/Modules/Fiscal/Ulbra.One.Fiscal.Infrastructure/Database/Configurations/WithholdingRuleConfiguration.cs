using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Common.Infrastructure.Persistence;
using Ulbra.One.Fiscal.Domain.Entities;

namespace Ulbra.One.Fiscal.Infrastructure.Database.Configurations;

public sealed class WithholdingRuleConfiguration : BaseEntityConfiguration<WithholdingRule>
{
    protected override string Schema => Schemas.Fiscal;

    protected override void ConfigureEntity(EntityTypeBuilder<WithholdingRule> e)
    {
        e.HasKey(x => x.Id);

        // Enums stored as strings for readability/stability.
        e.Property(x => x.TaxType).HasConversion<string>().HasMaxLength(20);
        e.Property(x => x.VendorRegime).HasConversion<string>().HasMaxLength(20);
        e.Property(x => x.RateSource).HasConversion<string>().HasMaxLength(20);
        e.Property(x => x.BaseRule).HasConversion<string>().HasMaxLength(20);
        e.Property(x => x.Reason).HasConversion<string>().HasMaxLength(40);
        e.Property(x => x.SimplesAnexo).HasConversion<string>().HasMaxLength(4);

        e.Property(x => x.Lc116Subitem).HasMaxLength(10);
        e.Property(x => x.MunicipalityIbge).HasMaxLength(7);
        e.Property(x => x.Rate).HasPrecision(10, 6);
        e.Property(x => x.MinimumAmount).HasPrecision(18, 2);

        // Computed (no backing column) — keep out of the model.
        e.Ignore(x => x.Group);
        e.Ignore(x => x.DimensionCount);

        e.HasIndex(x => new { x.CompanyId, x.TaxType });
    }
}
