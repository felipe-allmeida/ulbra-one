using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Common.Infrastructure.Persistence;
using Ulbra.One.Identity.Domain.Entities;

namespace Ulbra.One.Identity.Infrastructure.Database.Configurations;

public sealed class UserConfiguration : BaseEntityConfiguration<User>
{
    protected override string Schema => Schemas.Identity;

    protected override void ConfigureEntity(EntityTypeBuilder<User> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.Email).HasMaxLength(256).IsRequired();
        e.Property(x => x.Name).HasMaxLength(200).IsRequired();
        e.Property(x => x.Role).HasMaxLength(50).IsRequired();
        e.Property(x => x.GoogleSubject).HasMaxLength(64);
        e.HasIndex(x => x.Email).IsUnique();
    }
}
