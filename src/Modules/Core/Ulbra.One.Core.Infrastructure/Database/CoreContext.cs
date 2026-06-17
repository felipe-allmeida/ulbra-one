using Microsoft.EntityFrameworkCore;
using Ulbra.One.Audit.Infrastructure;
using Ulbra.One.Core.Domain.Entities;

namespace Ulbra.One.Core.Infrastructure.Database;

public class CoreContext(DbContextOptions<CoreContext> options) : DbContext(options)
{
    public const string Schema = "core";

    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Unit> Units => Set<Unit>();
    public DbSet<CostCenter> CostCenters => Set<CostCenter>();
    public DbSet<CostCenterUnit> CostCenterUnits => Set<CostCenterUnit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CoreContext).Assembly);
        modelBuilder.MapAuditLog(ownsTable: false);
        base.OnModelCreating(modelBuilder);
    }
}
