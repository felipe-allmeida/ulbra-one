using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Common.Infrastructure.Persistence;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Infrastructure.Database.Configurations;

public sealed class InstallmentConfiguration : BaseEntityConfiguration<Installment>
{
    protected override string Schema => Schemas.Finance;

    protected override void ConfigureEntity(EntityTypeBuilder<Installment> e)
    {
        e.HasKey(x => x.Id);
        e.Property(x => x.Amount).HasPrecision(18, 2);
        e.Property(x => x.NetAmount).HasPrecision(18, 2);
        e.Property(x => x.PaidAmount).HasPrecision(18, 2);
        e.HasIndex(x => x.PayableId);
        e.HasIndex(x => x.DueDate);
        e.HasIndex(x => x.Status);
        // Payments are append-only: never cascade-delete a baixa. Restrict means an installment that
        // has payments cannot be deleted (the UpdatePayable guard blocks editing a payable with payments).
        e.HasMany(x => x.Payments).WithOne().HasForeignKey(p => p.InstallmentId).OnDelete(DeleteBehavior.Restrict);
        e.Navigation(x => x.Payments).AutoInclude();
        // Optimistic concurrency via Postgres' hidden xmin system column (no DDL created).
        // Critical: two concurrent RegisterPayment calls mutate the SAME installment row;
        // the token makes the second SaveChanges fail instead of double-paying real cash.
        e.Property<uint>("xmin").IsRowVersion().HasColumnName("xmin");
    }
}
