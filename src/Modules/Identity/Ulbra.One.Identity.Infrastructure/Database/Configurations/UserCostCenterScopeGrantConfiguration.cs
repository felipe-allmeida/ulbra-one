using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Common.Infrastructure.Persistence;
using Ulbra.One.Identity.Domain.Entities;

namespace Ulbra.One.Identity.Infrastructure.Database.Configurations;

public sealed class UserCostCenterScopeGrantConfiguration : BaseEntityConfiguration<UserCostCenterScopeGrant>
{
    protected override string Schema => Schemas.Identity;
    protected override string TableName => "user_cost_center_scope_grant";

    protected override void ConfigureEntity(EntityTypeBuilder<UserCostCenterScopeGrant> e)
    {
        e.HasKey(x => x.Id);

        e.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // One cost-center grant per (user, cost center). CompanyId denormalized (loose Guid) for claim emission.
        e.HasIndex(x => new { x.UserId, x.CostCenterId }).IsUnique();
        e.HasIndex(x => new { x.UserId, x.CompanyId });
    }
}
