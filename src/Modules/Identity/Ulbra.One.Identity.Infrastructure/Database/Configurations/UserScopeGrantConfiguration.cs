using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Common.Infrastructure.Persistence;
using Ulbra.One.Identity.Domain.Entities;

namespace Ulbra.One.Identity.Infrastructure.Database.Configurations;

public sealed class UserScopeGrantConfiguration : BaseEntityConfiguration<UserScopeGrant>
{
    protected override string Schema => Schemas.Identity;
    protected override string TableName => "user_scope_grant";

    protected override void ConfigureEntity(EntityTypeBuilder<UserScopeGrant> e)
    {
        e.HasKey(x => x.Id);

        // Real FK to the user (same identity schema); no navigation on User — the grants are
        // resolved by query at login, mirroring how role permissions are resolved.
        e.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // One grant per (user, company).
        e.HasIndex(x => new { x.UserId, x.CompanyId }).IsUnique();
    }
}
