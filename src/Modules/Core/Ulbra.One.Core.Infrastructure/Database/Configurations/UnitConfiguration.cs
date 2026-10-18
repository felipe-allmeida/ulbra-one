using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Common.Infrastructure.Persistence;
using Ulbra.One.Core.Domain.Entities;

namespace Ulbra.One.Core.Infrastructure.Database.Configurations;

public sealed class UnitConfiguration : BaseEntityConfiguration<Unit>
{
    protected override string Schema => Schemas.Core;
    protected override string TableName => "units";

    protected override void ConfigureEntity(EntityTypeBuilder<Unit> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.Name).HasMaxLength(200).IsRequired();
        e.Property(x => x.Acronym).HasMaxLength(20).IsRequired();
        e.Property(x => x.Code).HasMaxLength(20);
        e.Property(x => x.Cnpj).HasMaxLength(14);
        e.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => new { x.CompanyId, x.Acronym }).IsUnique();
        // Legacy ERP code is unique across units when present (stable natural key from the legacy system).
        e.HasIndex(x => x.Code).IsUnique().HasFilter("code IS NOT NULL");
        // Establishment CNPJ is unique across units when present (matriz + filiais differ by ordem+DV).
        e.HasIndex(x => x.Cnpj).IsUnique().HasFilter("cnpj IS NOT NULL");
    }
}
