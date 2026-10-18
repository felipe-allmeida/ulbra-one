using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Common.Infrastructure.Persistence;
using Ulbra.One.Identity.Domain.Entities;

namespace Ulbra.One.Identity.Infrastructure.Database.Configurations;

public sealed class RoleConfiguration : BaseEntityConfiguration<Role>
{
    protected override string Schema => Schemas.Identity;
    protected override string TableName => "role";

    protected override void ConfigureEntity(EntityTypeBuilder<Role> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.Name).HasMaxLength(50).IsRequired();
        e.HasIndex(x => x.Name).IsUnique();

        e.HasMany(x => x.Claims)
            .WithOne()
            .HasForeignKey(c => c.RoleId)
            .OnDelete(DeleteBehavior.Cascade);

        e.Navigation(x => x.Claims).UsePropertyAccessMode(PropertyAccessMode.Field);

        e.HasData(RoleSeed.Roles());
    }
}
