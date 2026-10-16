using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Common.Infrastructure.Persistence;
using Ulbra.One.Core.Domain.Entities;

namespace Ulbra.One.Core.Infrastructure.Database.Configurations;

public sealed class CompanyConfiguration : BaseEntityConfiguration<Company>
{
    protected override string Schema => Schemas.Core;
    protected override string TableName => "companies";

    protected override void ConfigureEntity(EntityTypeBuilder<Company> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.LegalName).HasMaxLength(200).IsRequired();
        e.Property(x => x.Cnpj).HasMaxLength(14).IsRequired();
        // Partial unique index: uniqueness applies only to live rows, so a CNPJ can be re-registered after
        // the company is soft-deleted (deleted_at set). Mirrors the DeletedAt query filter from BaseEntityConfiguration.
        e.HasIndex(x => x.Cnpj).IsUnique().HasFilter("deleted_at IS NULL");
    }
}
