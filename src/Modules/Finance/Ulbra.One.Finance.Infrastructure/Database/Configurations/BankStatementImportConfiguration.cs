using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Common.Infrastructure.Persistence;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Infrastructure.Database.Configurations;

public sealed class BankStatementImportConfiguration : BaseEntityConfiguration<BankStatementImport>
{
    protected override string Schema => "finance";

    protected override void ConfigureEntity(EntityTypeBuilder<BankStatementImport> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.FileName).HasMaxLength(260).IsRequired();
        e.HasIndex(x => new { x.CompanyId, x.BankAccountId });
    }
}
