using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Common.Infrastructure.Persistence;
using Ulbra.One.Core.Domain.Entities;

namespace Ulbra.One.Core.Infrastructure.Database.Configurations;

public sealed class CostCenterUnitBudgetConfiguration : BaseEntityConfiguration<CostCenterUnitBudget>
{
    protected override string Schema => Schemas.Core;
    protected override string TableName => "cost_center_unit_budgets";

    protected override void ConfigureEntity(EntityTypeBuilder<CostCenterUnitBudget> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.Competency).HasMaxLength(7).IsRequired();
        e.Property(x => x.MonthlyLimit).HasPrecision(18, 2);

        // Intra-Core FKs (same module — real relationships, unlike cross-module loose Guids).
        e.HasOne<CostCenter>().WithMany().HasForeignKey(x => x.CostCenterId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne<Unit>().WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);

        // One ceiling per (cost center, unit, month) — the enforceable natural key.
        e.HasIndex(x => new { x.CostCenterId, x.UnitId, x.Competency }).IsUnique();
        e.HasIndex(x => x.CompanyId);
    }
}
