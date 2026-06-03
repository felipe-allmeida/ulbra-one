using Microsoft.EntityFrameworkCore;
using Ulbra.One.Finance.Domain.Entities;

namespace Ulbra.One.Finance.Infrastructure.Database;

public class FinanceContext(DbContextOptions<FinanceContext> options) : DbContext(options)
{
    public const string Schema = "finance";

    public DbSet<Transaction> Transactions => Set<Transaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Transaction>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Description).HasMaxLength(300).IsRequired();
            e.Property(x => x.Counterparty).HasMaxLength(200);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.RejectionReason).HasMaxLength(500);
            // CompanyId/UnitId/CostCenterId: logical references to Core (no cross-context FK)
            e.HasIndex(x => x.CompanyId);
            e.HasIndex(x => x.CostCenterId);
            e.HasIndex(x => new { x.Type, x.Status });
        });

        base.OnModelCreating(modelBuilder);
    }
}
