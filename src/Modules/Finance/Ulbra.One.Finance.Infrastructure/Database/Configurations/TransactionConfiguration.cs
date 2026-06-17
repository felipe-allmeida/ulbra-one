using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Infrastructure.Database.Configurations;

public sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> e)
    {
        e.Metadata.SetSchema("finance");
        e.HasKey(x => x.Id);
        e.Property(x => x.Description).HasMaxLength(300).IsRequired();
        e.Property(x => x.CounterpartyName).HasMaxLength(200);
        e.Property(x => x.Amount).HasPrecision(18, 2);
        e.Property(x => x.RejectionReason).HasMaxLength(500);
        // CompanyId/UnitId/CostCenterId/VendorId/CustomerId: logical references to Core/Registry (no cross-context FK)
        e.HasIndex(x => x.CompanyId);
        e.HasIndex(x => x.CostCenterId);
        e.HasIndex(x => x.VendorId);
        e.HasIndex(x => x.CustomerId);
        e.HasIndex(x => new { x.Type, x.Status });
    }
}
