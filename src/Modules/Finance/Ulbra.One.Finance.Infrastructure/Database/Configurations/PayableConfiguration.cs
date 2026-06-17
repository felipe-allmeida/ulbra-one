using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Infrastructure.Database.Configurations;

public sealed class PayableConfiguration : IEntityTypeConfiguration<Payable>
{
    public void Configure(EntityTypeBuilder<Payable> e)
    {
        e.Metadata.SetSchema("finance");
        e.HasKey(x => x.Id);
        e.Property(x => x.CounterpartyName).HasMaxLength(200).IsRequired();
        e.Property(x => x.DocumentNumber).HasMaxLength(60);
        e.Property(x => x.Description).HasMaxLength(300).IsRequired();
        e.Property(x => x.TotalAmount).HasPrecision(18, 2);
        e.Property(x => x.RejectionReason).HasMaxLength(500);
        e.Property(x => x.CancelReason).HasMaxLength(500);
        e.Property(x => x.ApprovedBy).HasMaxLength(64);
        e.Property(x => x.RejectedBy).HasMaxLength(64);
        e.Property(x => x.CreatedBy).HasMaxLength(64);
        e.Property(x => x.UpdatedBy).HasMaxLength(64);
        e.HasMany(x => x.Installments).WithOne().HasForeignKey(i => i.PayableId).OnDelete(DeleteBehavior.Cascade);
        e.HasMany(x => x.Attachments).WithOne().HasForeignKey(a => a.PayableId).OnDelete(DeleteBehavior.Cascade);
        e.Navigation(x => x.Installments).AutoInclude();
        e.HasIndex(x => x.CompanyId);
        e.HasIndex(x => x.CostCenterId);
        e.HasIndex(x => x.VendorId);
        e.HasIndex(x => x.Status);
        e.HasQueryFilter(x => x.DeletedAt == null);
        // Optimistic concurrency via Postgres' hidden xmin system column (no DDL created).
        // Catches approve/cancel/update races on the Payable row.
        e.Property<uint>("xmin").IsRowVersion().HasColumnName("xmin");
    }
}
