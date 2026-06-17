using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Core.Domain.Entities;

namespace Ulbra.One.Core.Infrastructure.Database.Configurations;

public sealed class CompanyConfiguration : IEntityTypeConfiguration<Company>
{
    public void Configure(EntityTypeBuilder<Company> e)
    {
        e.Metadata.SetSchema("core");
        e.HasKey(x => x.Id);
        e.Property(x => x.LegalName).HasMaxLength(200).IsRequired();
        e.Property(x => x.Cnpj).HasMaxLength(14).IsRequired();
        e.HasIndex(x => x.Cnpj).IsUnique();
        e.Property(x => x.CreatedBy).HasMaxLength(64);
        e.Property(x => x.UpdatedBy).HasMaxLength(64);
    }
}
