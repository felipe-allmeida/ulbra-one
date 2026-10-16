using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Budgeting.Domain.Entities;
using Ulbra.One.Common.Infrastructure.Persistence;

namespace Ulbra.One.Budgeting.Infrastructure.Database.Configurations;

public sealed class ProjectConfiguration : BaseEntityConfiguration<Project>
{
    protected override string Schema => Schemas.Budgeting;
    protected override string TableName => "projects";

    protected override void ConfigureEntity(EntityTypeBuilder<Project> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.Name).HasMaxLength(200).IsRequired();
        e.Property(x => x.Description).HasMaxLength(2000);
        e.Property(x => x.BudgetLimit).HasPrecision(18, 2);
        e.Property(x => x.InitialConsumedValue).HasPrecision(18, 2);
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        // Cross-schema references are loose Guids — no HasOne/HasForeignKey.
        e.HasIndex(x => new { x.CompanyId, x.Name }).IsUnique();
        e.HasIndex(x => x.UnitId);
    }
}
