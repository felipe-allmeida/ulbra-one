using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Common.Infrastructure.Persistence;
using Ulbra.One.Core.Domain.Entities;

namespace Ulbra.One.Core.Infrastructure.Database.Configurations;

public sealed class CostCenterUnitConfiguration : BaseEntityConfiguration<CostCenterUnit>
{
    protected override string Schema => Schemas.Core;

    protected override void ConfigureEntity(EntityTypeBuilder<CostCenterUnit> e)
    {
        e.HasKey(x => new { x.CostCenterId, x.UnitId });
        e.HasOne<CostCenter>().WithMany().HasForeignKey(x => x.CostCenterId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne<Unit>().WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Cascade);
    }
}
