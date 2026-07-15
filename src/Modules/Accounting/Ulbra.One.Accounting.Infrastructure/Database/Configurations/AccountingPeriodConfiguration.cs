using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Accounting.Domain.Entities;
using Ulbra.One.Common.Infrastructure.Persistence;

namespace Ulbra.One.Accounting.Infrastructure.Database.Configurations;

public sealed class AccountingPeriodConfiguration : BaseEntityConfiguration<AccountingPeriod>
{
    protected override string Schema => Schemas.Accounting;

    protected override void ConfigureEntity(EntityTypeBuilder<AccountingPeriod> e)
    {
        e.HasKey(x => x.Id);
        // One period row per company + competence month.
        e.HasIndex(x => new { x.CompanyId, x.FiscalYear, x.Month }).IsUnique();
    }
}
