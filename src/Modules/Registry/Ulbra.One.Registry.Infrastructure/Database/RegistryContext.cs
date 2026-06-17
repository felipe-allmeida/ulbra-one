using Microsoft.EntityFrameworkCore;
using Ulbra.One.Audit.Infrastructure;
using Ulbra.One.Registry.Domain.Entities;

namespace Ulbra.One.Registry.Infrastructure.Database;

public class RegistryContext(DbContextOptions<RegistryContext> options) : DbContext(options)
{
    public const string Schema = "registry";

    public DbSet<VendorEntity> Vendors => Set<VendorEntity>();
    public DbSet<CustomerEntity> Customers => Set<CustomerEntity>();
    public DbSet<EmployeeEntity> Employees => Set<EmployeeEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(RegistryContext).Assembly);
        modelBuilder.MapAuditLog(ownsTable: false);
        base.OnModelCreating(modelBuilder);
    }
}
