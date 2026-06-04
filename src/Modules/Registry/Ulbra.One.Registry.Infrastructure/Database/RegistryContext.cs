using Microsoft.EntityFrameworkCore;
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
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<VendorEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.TradeName).HasMaxLength(200);
            e.Property(x => x.Document).HasMaxLength(14).IsRequired();
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.Phone).HasMaxLength(20);
            e.Property(x => x.PaymentTerms).HasMaxLength(100);
            e.Property(x => x.BankInfo).HasMaxLength(200);
            e.Property(x => x.CreatedBy).HasMaxLength(64);
            e.Property(x => x.UpdatedBy).HasMaxLength(64);
            e.OwnsOne(x => x.Address, ConfigureAddress);
            e.HasIndex(x => new { x.CompanyId, x.Document }).IsUnique();
            e.HasQueryFilter(x => x.DeletedAt == null);
        });

        modelBuilder.Entity<CustomerEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.TradeName).HasMaxLength(200);
            e.Property(x => x.Document).HasMaxLength(14).IsRequired();
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.Phone).HasMaxLength(20);
            e.Property(x => x.CreditLimit).HasPrecision(18, 2);
            e.Property(x => x.CreatedBy).HasMaxLength(64);
            e.Property(x => x.UpdatedBy).HasMaxLength(64);
            e.OwnsOne(x => x.Address, ConfigureAddress);
            e.HasIndex(x => new { x.CompanyId, x.Document }).IsUnique();
            e.HasQueryFilter(x => x.DeletedAt == null);
        });

        modelBuilder.Entity<EmployeeEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Document).HasMaxLength(11).IsRequired();
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.Phone).HasMaxLength(20);
            e.Property(x => x.Role).HasMaxLength(100).IsRequired();
            e.Property(x => x.CreatedBy).HasMaxLength(64);
            e.Property(x => x.UpdatedBy).HasMaxLength(64);
            e.OwnsOne(x => x.Address, ConfigureAddress);
            e.HasIndex(x => new { x.CompanyId, x.Document }).IsUnique();
            e.HasQueryFilter(x => x.DeletedAt == null);
        });

        base.OnModelCreating(modelBuilder);
    }

    private static void ConfigureAddress<T>(
        Microsoft.EntityFrameworkCore.Metadata.Builders.OwnedNavigationBuilder<T, Domain.ValueObjects.Address> a)
        where T : class
    {
        a.Property(x => x.Cep).HasMaxLength(8);
        a.Property(x => x.Street).HasMaxLength(200);
        a.Property(x => x.Number).HasMaxLength(20);
        a.Property(x => x.Complement).HasMaxLength(100);
        a.Property(x => x.Neighborhood).HasMaxLength(100);
        a.Property(x => x.City).HasMaxLength(100);
        a.Property(x => x.State).HasMaxLength(2);
    }
}
