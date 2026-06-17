using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Infrastructure.Database.Configurations;

public sealed class ClassificationPolicyConfiguration : IEntityTypeConfiguration<ClassificationPolicy>
{
    public void Configure(EntityTypeBuilder<ClassificationPolicy> e)
    {
        e.Metadata.SetSchema("finance");
        e.HasKey(x => x.Id);
        e.HasIndex(x => x.CompanyId).IsUnique();
    }
}
