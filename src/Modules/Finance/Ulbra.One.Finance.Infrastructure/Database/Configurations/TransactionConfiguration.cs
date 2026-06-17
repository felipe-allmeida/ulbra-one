using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ulbra.One.Common.Infrastructure.Persistence;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Infrastructure.Database.Configurations;

public sealed class TransactionConfiguration : BaseEntityConfiguration<Transaction>
{
    protected override string Schema => Schemas.Finance;

    // Transaction is ISoftDeletable but had no filter historically — preserve that behavior.
    protected override bool ApplySoftDeleteFilter => false;

    protected override void ConfigureEntity(EntityTypeBuilder<Transaction> e)
    {
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
