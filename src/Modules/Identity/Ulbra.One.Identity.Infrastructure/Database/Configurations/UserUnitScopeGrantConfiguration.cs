using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Common.Infrastructure.Persistence;
using Ulbra.One.Identity.Domain.Entities;

namespace Ulbra.One.Identity.Infrastructure.Database.Configurations;

public sealed class UserUnitScopeGrantConfiguration : BaseEntityConfiguration<UserUnitScopeGrant>
{
    protected override string Schema => Schemas.Identity;
    protected override string TableName => "user_unit_scope_grant";

    protected override void ConfigureEntity(EntityTypeBuilder<UserUnitScopeGrant> e)
    {
        e.HasKey(x => x.Id);

        // Real FK to the user (same identity schema); no navigation on User — resolved by query at login.
        e.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // One unit grant per (user, unit). CompanyId is denormalized (loose Guid) for per-company claim emission.
        e.HasIndex(x => new { x.UserId, x.UnitId }).IsUnique();
        e.HasIndex(x => new { x.UserId, x.CompanyId });
    }
}
