using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Infrastructure.Database.Configurations;

public sealed class ReceivableConfiguration : IEntityTypeConfiguration<Receivable>
{
    public void Configure(EntityTypeBuilder<Receivable> e)
    {
        e.Metadata.SetSchema("finance");
        e.HasKey(x => x.Id);
        e.Property(x => x.PayerName).HasMaxLength(200).IsRequired();
        e.Property(x => x.DocumentNumber).HasMaxLength(60);
        e.Property(x => x.Description).HasMaxLength(300).IsRequired();
        e.Property(x => x.TotalAmount).HasPrecision(18, 2);
        e.Property(x => x.CancelReason).HasMaxLength(500);
        e.Property(x => x.WriteOffReason).HasMaxLength(500);
        e.Property(x => x.CreatedBy).HasMaxLength(64);
        e.Property(x => x.UpdatedBy).HasMaxLength(64);
        // No AutoInclude here (unlike Payable): keeps the ~19k-title list cheap. Detail/baixa endpoints Include explicitly.
        e.HasMany(x => x.Installments).WithOne().HasForeignKey(i => i.ReceivableId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => x.CompanyId);
        e.HasIndex(x => x.CostCenterId);
        e.HasIndex(x => x.PayerId);
        e.HasIndex(x => x.Status);
        e.HasQueryFilter(x => x.DeletedAt == null);
    }
}
