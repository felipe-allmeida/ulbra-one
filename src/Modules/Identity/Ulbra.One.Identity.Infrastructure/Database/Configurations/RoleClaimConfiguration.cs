using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Common.Infrastructure.Persistence;
using Ulbra.One.Identity.Domain.Entities;

namespace Ulbra.One.Identity.Infrastructure.Database.Configurations;

public sealed class RoleClaimConfiguration : BaseEntityConfiguration<RoleClaim>
{
    protected override string Schema => Schemas.Identity;
    protected override string TableName => "role_claim";

    protected override void ConfigureEntity(EntityTypeBuilder<RoleClaim> e)
    {
        e.HasKey(x => new { x.RoleId, x.Permission });
        e.Property(x => x.Permission).HasMaxLength(100).IsRequired();

        e.HasData(RoleSeed.Claims());
    }
}
