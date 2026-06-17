using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Common.Infrastructure.Persistence;
using Ulbra.One.Core.Domain.Entities;

namespace Ulbra.One.Core.Infrastructure.Database.Configurations;

public sealed class CompanyConfiguration : BaseEntityConfiguration<Company>
{
    protected override string Schema => "core";

    protected override void ConfigureEntity(EntityTypeBuilder<Company> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.LegalName).HasMaxLength(200).IsRequired();
        e.Property(x => x.Cnpj).HasMaxLength(14).IsRequired();
        e.HasIndex(x => x.Cnpj).IsUnique();
    }
}
