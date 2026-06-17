using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Infrastructure.Database.Configurations;

public sealed class BankTransactionConfiguration : IEntityTypeConfiguration<BankTransaction>
{
    public void Configure(EntityTypeBuilder<BankTransaction> e)
    {
        e.Metadata.SetSchema("finance");
        e.HasKey(x => x.Id);
        e.Property(x => x.ExternalId).HasMaxLength(120).IsRequired();
        e.Property(x => x.Amount).HasPrecision(18, 2);
        e.Property(x => x.Description).HasMaxLength(300);
        e.HasIndex(x => new { x.BankAccountId, x.ExternalId }).IsUnique();
        e.HasIndex(x => new { x.CompanyId, x.MatchStatus });
        e.HasIndex(x => x.ImportId);
        e.HasIndex(x => x.Date);
    }
}
