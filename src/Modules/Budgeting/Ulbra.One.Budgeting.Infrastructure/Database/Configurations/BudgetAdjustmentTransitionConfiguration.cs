using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Infrastructure.Persistence;

namespace Ulbra.One.Budgeting.Infrastructure.Database.Configurations;

public sealed class BudgetAdjustmentTransitionConfiguration : BaseEntityConfiguration<BudgetAdjustmentTransition>
{
    protected override string Schema => Schemas.Budgeting;
    protected override string TableName => "budget_adjustment_transition";

    protected override void ConfigureEntity(EntityTypeBuilder<BudgetAdjustmentTransition> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(32).IsRequired();
        e.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(32).IsRequired();
        e.Property(x => x.Action).HasConversion<string>().HasMaxLength(32).IsRequired();
        e.Property(x => x.Reason).HasMaxLength(1000);
        e.HasIndex(x => x.BudgetAdjustmentId);
    }
}
