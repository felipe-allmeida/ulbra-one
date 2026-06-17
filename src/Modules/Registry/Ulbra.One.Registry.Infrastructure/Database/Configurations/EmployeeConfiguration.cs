using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Registry.Infrastructure.Database.Configurations;

public sealed class EmployeeConfiguration : IEntityTypeConfiguration<EmployeeEntity>
{
    public void Configure(EntityTypeBuilder<EmployeeEntity> e)
    {
        e.Metadata.SetSchema("registry");
        e.HasKey(x => x.Id);
        e.Property(x => x.Name).HasMaxLength(200).IsRequired();
        e.Property(x => x.Document).HasMaxLength(11).IsRequired();
        e.Property(x => x.Email).HasMaxLength(200);
        e.Property(x => x.Phone).HasMaxLength(20);
        e.Property(x => x.Role).HasMaxLength(100).IsRequired();
        e.Property(x => x.CreatedBy).HasMaxLength(64);
        e.Property(x => x.UpdatedBy).HasMaxLength(64);
        e.OwnsOne(x => x.Address, AddressConfiguration.Configure);
        // Partial unique index: uniqueness applies only to live (non-soft-deleted) rows,
        // so the same document can be re-registered after a soft-delete.
        e.HasIndex(x => new { x.CompanyId, x.Document }).IsUnique().HasFilter("deleted_at IS NULL");
        e.HasQueryFilter(x => x.DeletedAt == null);
    }
}
