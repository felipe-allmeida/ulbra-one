using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Infrastructure.Persistence;

namespace Ulbra.One.Budgeting.Infrastructure.Database.Configurations;

public sealed class RequisitionTransitionConfiguration : BaseEntityConfiguration<RequisitionTransition>
{
    protected override string Schema => Schemas.Budgeting;
    protected override string TableName => "requisition_transition";

    protected override void ConfigureEntity(EntityTypeBuilder<RequisitionTransition> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(32).IsRequired();
        e.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(32).IsRequired();
        e.Property(x => x.Action).HasConversion<string>().HasMaxLength(32).IsRequired();
        e.Property(x => x.Reason).HasMaxLength(1000);
        e.HasIndex(x => x.RequisitionId);
        // Serves the SLA report's historical scan (candidates by stage entry in a window) — AEL-872.
        e.HasIndex(x => new { x.ToStatus, x.OccurredAt });
    }
}
