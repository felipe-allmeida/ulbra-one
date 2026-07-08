using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Infrastructure.Persistence;

namespace Ulbra.One.Accounting.Infrastructure.Database.Configurations;

public sealed class LedgerAccountConfiguration : BaseEntityConfiguration<LedgerAccount>
{
    protected override string Schema => Schemas.Accounting;

    protected override void ConfigureEntity(EntityTypeBuilder<LedgerAccount> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.Code).HasMaxLength(40).IsRequired();
        e.Property(x => x.Name).HasMaxLength(200).IsRequired();
        e.HasIndex(x => x.Code).IsUnique().HasFilter("deleted_at IS NULL");
        e.HasIndex(x => x.ParentId);
    }
}
