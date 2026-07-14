using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Infrastructure.Persistence;

namespace Ulbra.One.Accounting.Infrastructure.Database.Configurations;

public sealed class PendingAccountingPostingConfiguration : IEntityTypeConfiguration<PendingAccountingPosting>
{
    public void Configure(EntityTypeBuilder<PendingAccountingPosting> e)
    {
        e.Metadata.SetSchema(Schemas.Accounting);
        e.HasKey(x => x.Id);
        e.Property(x => x.SourceKey).HasMaxLength(120).IsRequired();
        e.Property(x => x.Reason).HasMaxLength(200).IsRequired();
        e.Property(x => x.IntendedCodes).HasMaxLength(200).IsRequired();
        e.Property(x => x.Amount).HasPrecision(18, 2);
        e.HasIndex(x => new { x.CompanyId, x.ResolvedAt });
        // Idempotency: at most one OPEN pending per (company, sourceKey). Resolved rows are excluded, so a new
        // pending can be re-parked after an earlier one was resolved.
        e.HasIndex(x => new { x.CompanyId, x.SourceKey })
            .IsUnique()
            .HasFilter("resolved_at IS NULL")
            .HasDatabaseName("ix_pending_accounting_postings_company_source_key_open");
    }
}
