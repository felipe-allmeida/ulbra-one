using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Infrastructure.Persistence;

namespace Ulbra.One.Budgeting.Infrastructure.Database.Configurations;

public sealed class BudgetAdjustmentConfiguration : BaseEntityConfiguration<BudgetAdjustment>
{
    protected override string Schema => Schemas.Budgeting;
    protected override string TableName => "budget_adjustments";

    protected override void ConfigureEntity(EntityTypeBuilder<BudgetAdjustment> e)
    {
        e.HasKey(x => x.Id);

        e.Property(x => x.Type).HasConversion<string>().HasMaxLength(32).IsRequired();
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        e.Property(x => x.Competency).HasMaxLength(7).IsRequired();
        e.Property(x => x.SourceCompetency).HasMaxLength(7);
        e.Property(x => x.Value).HasPrecision(18, 2);
        e.Property(x => x.Justification).HasMaxLength(1000).IsRequired();
        e.Property(x => x.Counterparts).HasMaxLength(1000);

        // Per-company friendly sequential code; unique so the endpoint's max+1 assignment races safely.
        e.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();

        // Cross-schema references (company/unit/cost center) are loose Guids — no HasOne/HasForeignKey.
        // Transition history is an owned collection within the aggregate (real intra-schema FK, cascade).
        e.HasMany(x => x.Transitions)
            .WithOne()
            .HasForeignKey(t => t.BudgetAdjustmentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
