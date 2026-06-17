using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Identity.Domain.Entities;

namespace Ulbra.One.Identity.Infrastructure.Database.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> e)
    {
        e.Metadata.SetSchema("identity");
        e.HasKey(x => x.Id);
        e.Property(x => x.Email).HasMaxLength(256).IsRequired();
        e.Property(x => x.Name).HasMaxLength(200).IsRequired();
        e.Property(x => x.Role).HasMaxLength(50).IsRequired();
        e.Property(x => x.GoogleSubject).HasMaxLength(64);
        e.HasIndex(x => x.Email).IsUnique();
        e.Property(x => x.CreatedBy).HasMaxLength(64);
        e.Property(x => x.UpdatedBy).HasMaxLength(64);
    }
}
