using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Infrastructure.Persistence;

namespace Ulbra.One.Accounting.Infrastructure.Database.Configurations;

public sealed class JournalEntryConfiguration : BaseEntityConfiguration<JournalEntry>
{
    protected override string Schema => Schemas.Accounting;

    protected override void ConfigureEntity(EntityTypeBuilder<JournalEntry> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.Description).HasMaxLength(500).IsRequired();
        e.Property(x => x.SourceReference).HasMaxLength(200);
        e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.JournalEntryId).OnDelete(DeleteBehavior.Cascade);
        e.Navigation(x => x.Lines).AutoInclude();
        e.Property(x => x.SourceKey).HasMaxLength(120);
        e.HasIndex(x => x.CompanyId);
        e.HasIndex(x => x.Status);
        e.HasIndex(x => x.CompetenceDate);
        // Sequential entry number is unique per company once posted (Draft rows have a null Number).
        e.HasIndex(x => new { x.CompanyId, x.Number }).IsUnique().HasFilter("number IS NOT NULL");
        // Idempotency: a system entry with a given SourceKey posts exactly once per company (non-deleted only).
        e.HasIndex(x => new { x.CompanyId, x.SourceKey })
            .IsUnique()
            .HasFilter("source_key IS NOT NULL AND deleted_at IS NULL")
            .HasDatabaseName("ix_journal_entries_company_source_key");
    }
}
