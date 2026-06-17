using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Common.Infrastructure.Persistence;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Infrastructure.Database.Configurations;

public sealed class ExpenseCategoryConfiguration : BaseEntityConfiguration<ExpenseCategory>
{
    protected override string Schema => "finance";

    protected override void ConfigureEntity(EntityTypeBuilder<ExpenseCategory> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.Name).HasMaxLength(120).IsRequired();
        e.HasIndex(x => x.Name).IsUnique().HasFilter("deleted_at IS NULL");
    }
}
