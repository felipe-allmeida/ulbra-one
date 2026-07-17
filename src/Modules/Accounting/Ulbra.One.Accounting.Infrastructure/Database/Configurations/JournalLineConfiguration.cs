using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Infrastructure.Persistence;

namespace Ulbra.One.Accounting.Infrastructure.Database.Configurations;

public sealed class JournalLineConfiguration : BaseEntityConfiguration<JournalLine>
{
    protected override string Schema => Schemas.Accounting;

    protected override void ConfigureEntity(EntityTypeBuilder<JournalLine> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.Amount).HasPrecision(18, 2);
        e.Property(x => x.Description).HasMaxLength(300);
        e.HasIndex(x => x.JournalEntryId);
        e.HasIndex(x => x.LedgerAccountId);
        e.HasIndex(x => x.CostCenterId);
        // Never delete a ledger account that has movement: line → account FK is Restrict.
        e.HasOne<LedgerAccount>().WithMany().HasForeignKey(x => x.LedgerAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}
