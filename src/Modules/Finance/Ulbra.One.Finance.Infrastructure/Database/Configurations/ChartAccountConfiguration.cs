using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Common.Infrastructure.Persistence;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Infrastructure.Database.Configurations;

public sealed class ChartAccountConfiguration : BaseEntityConfiguration<ChartAccount>
{
    protected override string Schema => "finance";

    protected override void ConfigureEntity(EntityTypeBuilder<ChartAccount> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.Code).HasMaxLength(40).IsRequired();
        e.Property(x => x.Name).HasMaxLength(200).IsRequired();
        e.HasIndex(x => new { x.ChartType, x.Code }).IsUnique().HasFilter("deleted_at IS NULL");
    }
}
