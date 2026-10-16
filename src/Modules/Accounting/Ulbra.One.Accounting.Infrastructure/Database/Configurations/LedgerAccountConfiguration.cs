using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Infrastructure.Persistence;

namespace Ulbra.One.Accounting.Infrastructure.Database.Configurations;

public sealed class LedgerAccountConfiguration : BaseEntityConfiguration<LedgerAccount>
{
    protected override string Schema => Schemas.Accounting;
    protected override string TableName => "ledger_accounts";

    protected override void ConfigureEntity(EntityTypeBuilder<LedgerAccount> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.Code).HasMaxLength(40).IsRequired();
        e.Property(x => x.ReducedCode).HasMaxLength(20);
        e.Property(x => x.Name).HasMaxLength(200).IsRequired();
        e.HasIndex(x => x.Code).IsUnique().HasFilter("deleted_at IS NULL");
        e.HasIndex(x => x.ReducedCode).IsUnique().HasFilter("reduced_code IS NOT NULL AND deleted_at IS NULL");
        e.HasIndex(x => x.ParentId);
    }
}
