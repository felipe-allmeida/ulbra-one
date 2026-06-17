using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Core.Domain.Entities;

namespace Ulbra.One.Core.Infrastructure.Database.Configurations;

public sealed class CostCenterConfiguration : IEntityTypeConfiguration<CostCenter>
{
    public void Configure(EntityTypeBuilder<CostCenter> e)
    {
        e.Metadata.SetSchema("core");
        e.HasKey(x => x.Id);
        e.Property(x => x.Code).HasMaxLength(50).IsRequired();
        e.Property(x => x.Name).HasMaxLength(200).IsRequired();
        e.Property(x => x.AnnualBudget).HasPrecision(18, 2);
        e.Property(x => x.AutoApprovalLimit).HasPrecision(18, 2);
        e.HasOne<Company>().WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        e.Property(x => x.CreatedBy).HasMaxLength(64);
        e.Property(x => x.UpdatedBy).HasMaxLength(64);
    }
}
