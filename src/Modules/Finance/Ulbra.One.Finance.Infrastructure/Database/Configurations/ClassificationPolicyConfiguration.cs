using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Common.Infrastructure.Persistence;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Infrastructure.Database.Configurations;

public sealed class ClassificationPolicyConfiguration : BaseEntityConfiguration<ClassificationPolicy>
{
    protected override string Schema => Schemas.Finance;
    protected override string TableName => "classification_policies";

    protected override void ConfigureEntity(EntityTypeBuilder<ClassificationPolicy> e)
    {
        e.HasKey(x => x.Id);
        e.HasIndex(x => x.CompanyId).IsUnique();
    }
}
