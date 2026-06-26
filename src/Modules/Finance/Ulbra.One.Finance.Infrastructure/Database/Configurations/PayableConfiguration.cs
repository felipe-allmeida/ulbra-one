using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Common.Infrastructure.Persistence;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Infrastructure.Database.Configurations;

public sealed class PayableConfiguration : BaseEntityConfiguration<Payable>
{
    protected override string Schema => Schemas.Finance;

    protected override void ConfigureEntity(EntityTypeBuilder<Payable> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.CounterpartyName).HasMaxLength(200).IsRequired();
        e.Property(x => x.DocumentNumber).HasMaxLength(60);
        e.Property(x => x.Description).HasMaxLength(300).IsRequired();
        e.Property(x => x.TotalAmount).HasPrecision(18, 2);
        e.Property(x => x.RejectionReason).HasMaxLength(500);
        e.Property(x => x.CancelReason).HasMaxLength(500);
        e.Property(x => x.ApprovedBy).HasMaxLength(64);
        e.Property(x => x.RejectedBy).HasMaxLength(64);
        e.HasMany(x => x.Installments).WithOne().HasForeignKey(i => i.PayableId).OnDelete(DeleteBehavior.Cascade);
        e.HasMany(x => x.Attachments).WithOne().HasForeignKey(a => a.PayableId).OnDelete(DeleteBehavior.Cascade);
        e.HasMany(x => x.Withholdings).WithOne().HasForeignKey(w => w.PayableId).OnDelete(DeleteBehavior.Cascade);
        e.Navigation(x => x.Installments).AutoInclude();
        e.Navigation(x => x.Withholdings).AutoInclude();
        e.HasIndex(x => x.CompanyId);
        e.HasIndex(x => x.CostCenterId);
        e.HasIndex(x => x.VendorId);
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        e.Property(x => x.ClassificationRationale).HasMaxLength(1000);
        e.HasIndex(x => x.Status);
        // Optimistic concurrency via Postgres' hidden xmin system column (no DDL created).
        // Catches approve/cancel/update races on the Payable row.
        e.Property<uint>("xmin").IsRowVersion().HasColumnName("xmin");
    }
}
