using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Core.Domain.Entities;

namespace Ulbra.One.Core.Infrastructure.Database.Configurations;

public sealed class CostCenterUnitConfiguration : IEntityTypeConfiguration<CostCenterUnit>
{
    public void Configure(EntityTypeBuilder<CostCenterUnit> e)
    {
        e.Metadata.SetSchema("core");
        e.HasKey(x => new { x.CostCenterId, x.UnitId });
        e.HasOne<CostCenter>().WithMany().HasForeignKey(x => x.CostCenterId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne<Unit>().WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Cascade);
    }
}
