using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Common.Infrastructure.Persistence;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Infrastructure.Database.Configurations;

public sealed class ReceivableInstallmentConfiguration : BaseEntityConfiguration<ReceivableInstallment>
{
    protected override string Schema => Schemas.Finance;
    protected override string TableName => "receivable_installments";

    protected override void ConfigureEntity(EntityTypeBuilder<ReceivableInstallment> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.Amount).HasPrecision(18, 2);
        e.Property(x => x.ReceivedAmount).HasPrecision(18, 2);
        e.HasIndex(x => x.ReceivableId);
        e.HasIndex(x => x.DueDate);
        e.HasIndex(x => x.Status);
        e.HasMany(x => x.Receipts).WithOne().HasForeignKey(r => r.InstallmentId).OnDelete(DeleteBehavior.Cascade);
        e.Navigation(x => x.Receipts).AutoInclude();
    }
}
