using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Infrastructure.Database.Configurations;

public sealed class ChartAccountConfiguration : IEntityTypeConfiguration<ChartAccount>
{
    public void Configure(EntityTypeBuilder<ChartAccount> e)
    {
        e.Metadata.SetSchema("finance");
        e.HasKey(x => x.Id);
        e.Property(x => x.Code).HasMaxLength(40).IsRequired();
        e.Property(x => x.Name).HasMaxLength(200).IsRequired();
        e.HasIndex(x => new { x.ChartType, x.Code }).IsUnique().HasFilter("deleted_at IS NULL");
        e.HasQueryFilter(x => x.DeletedAt == null);
    }
}
